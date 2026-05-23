public interface IRevertable
{
    bool IsTemporary { get; }
    void Revert(ILivable target, int? appliedAmount = null);
}
