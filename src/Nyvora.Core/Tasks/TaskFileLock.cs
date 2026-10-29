using System.Security.Cryptography;
using System.Text;

namespace Nyvora.Core.Tasks;

/// <summary>
/// Cross-process lock around reading and writing tasks.json, so the app and an MCP server running at the same
/// time never interleave a read-modify-write. Held only for the length of one file operation.
/// </summary>
public sealed class TaskFileLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _held;

    private TaskFileLock(Mutex mutex, bool held)
    {
        _mutex = mutex;
        _held = held;
    }

    /// <summary>True when the lock was obtained; false when another process held it for the whole timeout.</summary>
    public bool Acquired => _held;

    public static TaskFileLock Acquire(string tasksPath, TimeSpan timeout)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(tasksPath).ToLowerInvariant())))[..16];
        var mutex = new Mutex(false, @"Local\Nyvora.Tasks." + key);
        bool held;
        try
        {
            held = mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            held = true; // the previous owner died mid-operation; the file itself is always replaced atomically
        }

        return new TaskFileLock(mutex, held);
    }

    public void Dispose()
    {
        if (_held)
        {
            _mutex.ReleaseMutex();
            _held = false;
        }

        _mutex.Dispose();
    }
}
