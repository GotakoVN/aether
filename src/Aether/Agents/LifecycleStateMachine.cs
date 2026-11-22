namespace Aether.Agents;

/// <summary>
/// Lifecycle states of a memory piece (Memory Entity) in the FEOFALLS cognitive system.
/// </summary>
public enum MemoryLifecycleState
{
    /// <summary>Active memory: highly fresh, retrieved often.</summary>
    Active,
    /// <summary>Fading memory: accessed less over time, starting to lose importance.</summary>
    Decaying,
    /// <summary>Long-term archived memory, compressed to optimize context space.</summary>
    Archived,
    /// <summary>Consolidated memory: synthesized into the agent's foundational experience and knowledge.</summary>
    Consolidated
}

/// <summary>
/// Memory lifecycle state machine (LifecycleStateMachine).
/// Implements the state transition of memory entities: ACTIVE → DECAYING → ARCHIVED → CONSOLIDATED.
/// Computes the salience score of a decaying memory with the logarithm log(access_count + 1) and a time-based exponential.
/// </summary>
public sealed class LifecycleStateMachine
{
    private readonly BootConfig _config;

    /// <summary>
    /// Creates a LifecycleStateMachine from a BootConfig.
    /// </summary>
    /// <param name="config">Configuration that holds the day counts for memory state transitions.</param>
    public LifecycleStateMachine(BootConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Analyzes and computes the current state of a memory entity from its age and access frequency.
    /// </summary>
    /// <param name="createdAt">When the memory was created.</param>
    /// <param name="lastAccessed">The last time the memory was accessed.</param>
    /// <param name="accessCount">Total number of times the memory was accessed.</param>
    /// <returns>The new lifecycle state of the memory (Active, Decaying, or Archived).</returns>
    public MemoryLifecycleState ComputeState(
        DateTime createdAt,
        DateTime lastAccessed,
        int accessCount)
    {
        var sinceAccess = DateTime.UtcNow - lastAccessed;

        if (sinceAccess.TotalDays > _config.DecayingToArchivedDays)
            return MemoryLifecycleState.Archived;

        if (sinceAccess.TotalDays > _config.ActiveToDecayingDays && accessCount < 2)
            return MemoryLifecycleState.Decaying;

        return MemoryLifecycleState.Active;
    }

    /// <summary>
    /// Computes the salience score of a memory. It represents the importance of the information
    /// and is used to filter memories when assembling the context.
    /// </summary>
    /// <param name="accessCount">Number of times the memory was accessed.</param>
    /// <param name="lastAccessAge">Age of the last access.</param>
    /// <returns>A salience score in the range [0.0, 1.0].</returns>
    public double ComputeSalience(int accessCount, TimeSpan lastAccessAge)
    {
        var rawScore = Math.Log2(accessCount + 1) / Math.Log2(100);
        var decayDays = Math.Max(0, lastAccessAge.TotalDays);
        var decay = Math.Exp(-decayDays / 90.0);
        return Math.Clamp(rawScore * decay, 0.0, 1.0);
    }
}
