using System.Text;
using Aether.Providers;

namespace Aether.Agent;

/// <summary>
/// Minimal working context of an agent (WorkingContext).
/// This class directly manages the live conversation, holding the System Prompt and the full message history in temporary memory.
/// It implements the NanoClaw/OpenClaw design philosophy: the conversation IS the context.
/// </summary>
public sealed class WorkingContext
{
    private readonly List<LlmMessage> _messages = new();
    private string _systemPrompt;

    /// <summary>
    /// Identifier of the current working session (Session ID), generated randomly or assigned by the SessionManager.
    /// </summary>
    public string SessionId { get; private set; }

    /// <summary>
    /// Path of the agent's working directory.
    /// </summary>
    public string WorkspacePath { get; }

    /// <summary>
    /// The full history of conversation messages (LlmMessage) held in memory.
    /// </summary>
    public IReadOnlyList<LlmMessage> Messages => _messages;

    /// <summary>
    /// The tools equipped for the agent in this working context.
    /// </summary>
    public IReadOnlyList<LlmTool> Tools { get; }

    /// <summary>
    /// Creates a new WorkingContext for the agent.
    /// </summary>
    /// <param name="workspacePath">Absolute working directory of the agent.</param>
    /// <param name="tools">List of the equipped tools.</param>
    public WorkingContext(string workspacePath, IReadOnlyList<LlmTool> tools)
    {
        SessionId = Guid.NewGuid().ToString("N");
        WorkspacePath = workspacePath;
        Tools = tools;
        _systemPrompt = BuildDefaultSystemPrompt(workspacePath);
        _messages.Add(LlmMessage.System(_systemPrompt));
    }

    /// <summary>
    /// Assigns a specific working session identifier.
    /// </summary>
    /// <param name="sessionId">Working session identifier.</param>
    public void SetSessionId(string sessionId)
    {
        SessionId = sessionId;
    }

    /// <summary>
    /// Resets the agent's core System Prompt and updates it, or inserts it at the top of the message list.
    /// </summary>
    /// <param name="prompt">New System Prompt content.</param>
    public void SetSystemPrompt(string prompt)
    {
        _systemPrompt = prompt;
        if (_messages.Count > 0 && _messages[0].Role == "system")
            _messages[0] = LlmMessage.System(prompt);
        else
            _messages.Insert(0, LlmMessage.System(prompt));
    }

    /// <summary>
    /// Records a new user message (User Message) in the conversation history.
    /// </summary>
    /// <param name="content">User message content.</param>
    public void AddUser(string content)
    {
        if (_messages.Count == 0 || _messages[0].Role != "system")
            _messages.Insert(0, LlmMessage.System(_systemPrompt));
        _messages.Add(LlmMessage.User(content));
    }

    /// <summary>
    /// Records a new agent reply (Assistant Message) in the conversation history.
    /// </summary>
    /// <param name="content">Agent reply content.</param>
    public void AddAssistant(string content)
    {
        _messages.Add(new LlmMessage("assistant", content));
    }

    /// <summary>
    /// Records the tool-call requests (Tool Calls) from the LLM in the conversation history.
    /// </summary>
    /// <param name="content">Accompanying explanatory text, if any.</param>
    /// <param name="toolCalls">List of the tools that were requested.</param>
    public void AddAssistantToolCalls(string content, IReadOnlyList<LlmToolCall> toolCalls)
    {
        _messages.Add(LlmMessage.AssistantToolCalls(content, toolCalls));
    }

    /// <summary>
    /// Records the result of a tool (Tool Result) after it ran, whether it succeeded or failed.
    /// </summary>
    /// <param name="toolCallId">Identifier of the matching tool call.</param>
    /// <param name="toolName">Name of the tool that just ran.</param>
    /// <param name="content">Output content of the tool.</param>
    public void AddToolResult(string toolCallId, string toolName, string content)
    {
        _messages.Add(LlmMessage.ToolResult(toolCallId, toolName, content));
    }

    /// <summary>
    /// Clears the whole message history held in memory and generates a brand new random Session ID.
    /// </summary>
    public void Reset()
    {
        SessionId = Guid.NewGuid().ToString("N");
        _messages.Clear();
    }

    /// <summary>
    /// Compacts the conversation (Context Compaction) to avoid overflowing the LLM's token limit.
    /// Automatically removes older messages in the middle of the conversation, keeping the System Prompt at the top and recent messages at the end.
    /// </summary>
    /// <param name="maxTokens">Maximum token limit.</param>
    public void Compact(int maxTokens)
    {
        while (_messages.Count > 2 && EstimateTokens(_messages) > maxTokens)
            _messages.RemoveAt(1);
    }

    /// <summary>
    /// Estimates the total tokens consumed by the whole message history held in memory.
    /// </summary>
    private static int EstimateTokens(IReadOnlyList<LlmMessage> messages)
    {
        var chars = 0;
        foreach (var m in messages)
        {
            chars += m.Content?.Length ?? 0;
            if (m.ToolCalls is not null)
                foreach (var tc in m.ToolCalls)
                {
                    chars += tc.Name.Length + tc.Id.Length;
                    foreach (var (k, v) in tc.Arguments)
                        chars += k.Length + v.Length;
                }
        }
        return Math.Max(1, chars / 4);
    }

    /// <summary>
    /// Builds the slim default System Prompt that guides the agent's behavior.
    /// </summary>
    private static string BuildDefaultSystemPrompt(string workspacePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are Aether, a working agent.");
        sb.AppendLine($"Workspace: {workspacePath}");
        sb.AppendLine();
        sb.AppendLine("## Rules");
        sb.AppendLine("- Act immediately. Don't describe — do.");
        sb.AppendLine("- Read before write/edit. Minimal scope.");
        sb.AppendLine("- Deliver evidence, not promises.");
        sb.AppendLine();
        sb.AppendLine("## Safety");
        sb.AppendLine("Refuse: self-harm, illegal activity, data exfiltration, destructive commands without confirmation.");
        return sb.ToString();
    }
}
