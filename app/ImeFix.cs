using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;

namespace OpenPaint3D;

/// <summary>
/// Avalonia 11/12 hands IBus the X11 keycode, but IBus expects the evdev code (X11 keycode - 8),
/// as GTK and Qt send it. ibus-hangul looks non-character keys up by that code, so Enter
/// (X11 36 = evdev KEY_J) typed "ㅓ" and Backspace / Tab other jamo. This wraps the IBus input
/// method so every key reaches IBus with the evdev code. Everything else is passed through.
/// </summary>
internal static class IBusKeycodeFix
{
    private const int EvdevOffset = 8;

    public static void Install()
    {
        try
        {
            var asm = Assembly.Load("Avalonia.FreeDesktop");
            var factoryType = asm.GetType("Avalonia.FreeDesktop.IX11InputMethodFactory");
            var controlType = asm.GetType("Avalonia.FreeDesktop.IX11InputMethodControl");
            if (factoryType == null || controlType == null) return;
            // the locator's members are not part of Avalonia's public API, so go through reflection
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var locatorType = typeof(AvaloniaLocator);
            var locator = locatorType.GetProperty("Current", any)!.GetValue(null)!;
            var current = locatorType.GetMethod("GetService", any, new[] { typeof(Type) })!.Invoke(locator, new object[] { factoryType });
            // only the IBus backend has the bug (Fcitx is given what it expects)
            if (current == null || !current.GetType().ToString().Contains("IBus")) return;
            var proxy = DispatchProxy.Create(factoryType, typeof(FactoryProxy));
            ((FactoryProxy)proxy).Init(current, controlType);
            var mutable = locatorType.GetProperty("CurrentMutable", any)!.GetValue(null)!;
            var bind = locatorType.GetMethods(any).First(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0).MakeGenericMethod(factoryType);
            var helper = bind.Invoke(mutable, null)!;
            helper.GetType().GetMethods(any).First(m => m.Name == "ToConstant" && m.IsGenericMethodDefinition)
                .MakeGenericMethod(factoryType).Invoke(helper, new[] { proxy });
            Installed = true;
        }
        catch (Exception)
        {
            // Avalonia internals changed: keep its own input method rather than failing to start
        }
    }

    public static bool Installed { get; private set; }

    /// <summary>Wraps CreateClient so the control half of each new client gets the fix.</summary>
    public class FactoryProxy : DispatchProxy
    {
        private object _inner = null!;
        private Type _controlType = null!;

        public void Init(object inner, Type controlType) => (_inner, _controlType) = (inner, controlType);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(_inner, args);
            if (method.Name != "CreateClient" || result is not ITuple pair || pair.Length != 2 || pair[1] is not { } control) return result;
            var wrapped = DispatchProxy.Create(_controlType, typeof(ControlProxy));
            ((ControlProxy)wrapped).Init(control);
            // rebuild the (method, control) value tuple with the wrapped control
            return Activator.CreateInstance(result.GetType(), pair[0], wrapped);
        }
    }

    public class ControlProxy : DispatchProxy
    {
        private object _inner = null!;

        public void Init(object inner) => _inner = inner;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "HandleEventAsync" && args is { Length: 3 } && args[2] is int keyCode && keyCode >= EvdevOffset)
                args[2] = keyCode - EvdevOffset;
            try
            {
                return method.Invoke(_inner, args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(e.InnerException);
                throw;
            }
        }
    }
}
