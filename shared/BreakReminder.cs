namespace BloxCode.Core;

/// <summary>
/// "Time for a break?": counts continuous play (any game, any window) and says when another
/// <paramref name="every"/> has gone by. Ten minutes without a game starts the count over, so a
/// quick restart or a teleport doesn't.
/// </summary>
public sealed class BreakReminder(TimeSpan every)
{
    public static readonly TimeSpan Pause = TimeSpan.FromMinutes(10);

    private DateTime? _since;
    private DateTime? _idleSince;
    private int _reminded;

    /// <summary>Call regularly; true when it is time to remind, with how long play has gone on.</summary>
    public bool Tick(bool playing, DateTime now, out TimeSpan played)
    {
        played = TimeSpan.Zero;
        if (every <= TimeSpan.Zero)
            return false;

        if (!playing)
        {
            _idleSince ??= now;
            if (now - _idleSince >= Pause)
            {
                _since = null;
                _reminded = 0;
            }
            return false;
        }

        _idleSince = null;
        _since ??= now;
        played = now - _since.Value;
        if (played < every * (_reminded + 1))
            return false;
        _reminded++;
        return true;
    }
}
