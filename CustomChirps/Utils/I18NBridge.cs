#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CustomChirps.Utils;

public static class I18NBridge
{
    private const string TargetAsmName = "I18NEverywhere";
    private const string TargetTypeName = "I18NEverywhere.I18NEverywhere";
    private const string TargetPropName = "CurrentLocaleDictionary";

    private static volatile Func<Dictionary<string, string>>? _getter;

    public static Dictionary<string, string>? GetDictionary()
    {
        var g = _getter ?? EnsureGetter();
        return g?.Invoke();
    }

    private static Func<Dictionary<string, string>>? EnsureGetter()
    {
        if (_getter is not null) return _getter;

        const string qualified = $"{TargetTypeName}, {TargetAsmName}";
        var type = Type.GetType(qualified, throwOnError: false);

        if (type == null)
        {
            var asm = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, TargetAsmName, StringComparison.Ordinal));
            if (asm != null)
                type = asm.GetType(TargetTypeName, throwOnError: false, ignoreCase: false);
        }

        if (type == null) return _getter = null;

        var prop = type.GetProperty(TargetPropName, BindingFlags.Public | BindingFlags.Static);
        var getMethod = prop?.GetGetMethod(nonPublic: false);
        if (getMethod == null) return _getter = null;

        _getter = (Func<Dictionary<string, string>>)Delegate.CreateDelegate(
            typeof(Func<Dictionary<string, string>>), getMethod);

        return _getter;
    }
}