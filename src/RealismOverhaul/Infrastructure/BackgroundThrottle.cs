using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace RealismOverhaul.Infrastructure;

/// Lowers the game's frame rate while it is unfocused or minimized, and restores the game's own
/// settings when it is focused again.
///
/// A game left open in the background keeps rendering and simulating; uncapped, that is a
/// sustained full load on the CPU and GPU for no benefit. This does not fix unstable hardware, but
/// it removes the load while nobody is looking. Optionally caps the foreground frame rate too.
public class BackgroundThrottleBehaviour : MonoBehaviour
{
    public BackgroundThrottleBehaviour(IntPtr ptr) : base(ptr) { }

    bool _throttled;
    int _savedFps;
    int _savedVSync;
    float _nextCheck;

    void Update()
    {
        // unscaledTime keeps ticking even if the game pauses its own time.
        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 1f;

        var focused = Application.isFocused;
        if (!focused && !_throttled)
        {
            _savedFps = Application.targetFrameRate;
            _savedVSync = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0; // targetFrameRate is ignored while vSync is on
            Application.targetFrameRate = Plugin.Flags.BackgroundFps.Value;
            _throttled = true;
            Plugin.Log.LogInfo($"[Throttle] game unfocused: capped at {Plugin.Flags.BackgroundFps.Value} FPS");
        }
        else if (focused && _throttled)
        {
            QualitySettings.vSyncCount = _savedVSync;
            Application.targetFrameRate = _savedFps;
            _throttled = false;
            Plugin.Log.LogInfo($"[Throttle] game focused: restored targetFrameRate={_savedFps}, vSyncCount={_savedVSync}");
            BackgroundThrottle.ApplyForegroundCap();
        }
    }
}

public static class BackgroundThrottle
{
    public static void Start()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<BackgroundThrottleBehaviour>();
            var go = new GameObject("RealismOverhaul.BackgroundThrottle");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<BackgroundThrottleBehaviour>();
            Plugin.Log.LogInfo($"[Throttle] started; game settings: runInBackground={Application.runInBackground}, " +
                               $"targetFrameRate={Application.targetFrameRate}, vSyncCount={QualitySettings.vSyncCount}");
            ApplyForegroundCap();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[Throttle] could not start: {e}");
        }
    }

    /// Optional cap while focused (0 = leave the game's own setting).
    public static void ApplyForegroundCap()
    {
        var cap = Plugin.Flags.ForegroundFpsCap.Value;
        if (cap <= 0) return;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = cap;
    }
}
