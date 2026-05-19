using System;

public interface IPathBlocker
{
    bool IsActive { get; }
    event Action OnDeactivated;
}
