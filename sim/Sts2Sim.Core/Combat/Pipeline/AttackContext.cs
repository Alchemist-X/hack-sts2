namespace Sts2Sim.Core.Combat.Pipeline;

public sealed class AttackContext : IDisposable
{
    private readonly CombatState _combatState;
    private readonly AttackCommand _attackCommand;
    private bool _disposed;

    private AttackContext(CombatState cs, CardPlay cardPlay)
    {
        _combatState = cs;
        _attackCommand = DamageCmd.Attack(cs, 0m).FromCard(cardPlay.Card, cardPlay).TargetingAllOpponents();
    }

    public AttackCommand Command => _attackCommand;

    public static AttackContext CreateContext(CombatState cs, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(cardPlay);
        var context = new AttackContext(cs, cardPlay);
        Hook.BeforeAttack(cs, context._attackCommand);
        return context;
    }

    public void AddHit(IReadOnlyList<DamageResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        _attackCommand.IncrementHitsInternal();
        _attackCommand.AddResultsInternal(results);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Hook.AfterAttack(_combatState, _attackCommand);
    }
}
