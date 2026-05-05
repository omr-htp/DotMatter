namespace DotMatter.Core.Sessions;

/// <summary>
/// Sliding window for message counter validation per Matter spec §4.7.2.
/// Tracks a window of 32 counters to allow out-of-order delivery while
/// rejecting duplicates and replays.
/// </summary>
internal class MessageCounterWindow
{
    private uint _baseCounter;
    private uint _bitmap; // bit 0 = _baseCounter, bit 31 = _baseCounter + 31
    private bool _initialized;

    /// <summary>
    /// Validates an incoming message counter. Returns true if the counter
    /// is acceptable (new message), false if it's a duplicate or replay.
    /// </summary>
    public bool Validate(uint counter)
    {
        if (!_initialized)
        {
            _baseCounter = counter;
            _bitmap = 1; // Mark base counter as received
            _initialized = true;
            return true;
        }

        uint forwardDistance = counter - _baseCounter;

        if (forwardDistance >= 0x8000_0000u)
        {
            // Counter is before the window — replay or too-old message
            return false;
        }

        if (forwardDistance < 32)
        {
            // Counter is within the window
            uint bit = 1u << (int)forwardDistance;
            if ((_bitmap & bit) != 0)
            {
                return false; // Already received — duplicate
            }

            _bitmap |= bit;
            return true;
        }

        // Counter is beyond the window — advance the window
        uint shift = forwardDistance - 31;
        if (shift >= 32)
        {
            // Jumped so far ahead that the entire window is new
            _bitmap = 0;
        }
        else
        {
            _bitmap >>= (int)shift;
        }

        _baseCounter += (uint)shift;
        // Mark the new counter (now at bit position 31)
        _bitmap |= 1u << (int)(counter - _baseCounter);
        return true;
    }

    /// <summary>
    /// Resets the window state (e.g., on session re-establishment).
    /// </summary>
    public void Reset()
    {
        _baseCounter = 0;
        _bitmap = 0;
        _initialized = false;
    }
}
