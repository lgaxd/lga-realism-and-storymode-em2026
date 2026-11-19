using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace RealismOverhaul.Infrastructure;

/// Startup check that class injection and delegate conversion work on this Unity version.
/// Both need Il2CppInterop's native struct layouts to match the game (see ADR 0001); they are
/// what custom UI (our own MonoBehaviours, button callbacks) depends on.
public class SelfTestBehaviour : MonoBehaviour
{
    public SelfTestBehaviour(IntPtr ptr) : base(ptr) { }

    int _frames;

    void Update()
    {
        if (++_frames != 60) return;
        Plugin.Log.LogInfo("[SelfTest] injected MonoBehaviour.Update is running (60 frames)");
        Destroy(gameObject);
    }
}

public static class ClassInjectionSelfTest
{
    public static void Run()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<SelfTestBehaviour>();
            var go = new GameObject("RealismOverhaul.SelfTest");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SelfTestBehaviour>();
            Plugin.Log.LogInfo("[SelfTest] class injection OK (MonoBehaviour registered and attached)");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[SelfTest] class injection FAILED: {e}");
        }

        try
        {
            var called = false;
            Il2CppSystem.Action action = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => called = true));
            action.Invoke();
            Plugin.Log.LogInfo($"[SelfTest] delegate conversion {(called ? "OK" : "FAILED (not invoked)")}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[SelfTest] delegate conversion FAILED: {e}");
        }
    }
}
