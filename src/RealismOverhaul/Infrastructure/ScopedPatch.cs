using System.Reflection;
using HarmonyLib;

namespace RealismOverhaul.Infrastructure;

/// A Harmony postfix that is installed only while a scope is open, and removed when the last
/// scope closes. Use it to change a hot, widely used game method (a getter, a query) only
/// during one specific game operation, instead of slowing it down everywhere.
///
/// Scopes nest: Begin/End pairs may overlap; the patch stays until the outermost End.
public sealed class ScopedPatch(Harmony harmony, MethodInfo target, MethodInfo postfix)
{
    readonly HarmonyMethod _postfix = new(postfix);
    int _depth;

    public bool IsOpen => _depth > 0;

    public void Begin()
    {
        if (_depth++ == 0)
            harmony.Patch(target, postfix: _postfix);
    }

    public void End()
    {
        if (_depth == 0) return;
        if (--_depth == 0)
            harmony.Unpatch(target, postfix);
    }
}
