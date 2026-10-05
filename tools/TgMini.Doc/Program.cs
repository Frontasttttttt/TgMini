// Разовый дамп: точная сигнатура LoginWithQRCode и типа Helpers.Log в WTelegramClient 4.4.9.
using System.Reflection;

var asm = typeof(WTelegram.Client).Assembly;
var cl = asm.GetType("WTelegram.Client")!;
foreach (var m in cl.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "LoginWithQRCode"))
{
    var r = m.ReturnType;
    var ts = r.IsGenericType
        ? $"{r.Name.Split('`')[0]}<{string.Join(",", r.GetGenericArguments().Select(g => g.FullName))}>"
        : r.FullName;
    Console.WriteLine($"LoginWithQRCode -> {ts}");
    foreach (var p in m.GetParameters())
    {
        var pt = p.ParameterType;
        var ps = pt.IsGenericType
            ? $"{pt.Name.Split('`')[0]}<{string.Join(",", pt.GetGenericArguments().Select(g => g.FullName))}>"
            : pt.FullName;
        Console.WriteLine($"  param {p.Name}: {ps}" + (p.HasDefaultValue ? $" = {p.DefaultValue ?? "null"}" : ""));
    }
}
var helpers = asm.GetType("WTelegram.Helpers")!;
var logProp = helpers.GetProperty("Log", BindingFlags.Public | BindingFlags.Static);
Console.WriteLine($"Helpers.Log type: {logProp?.PropertyType.FullName}");
var logType = logProp?.PropertyType;
if (logType is not null && logType.IsGenericType)
    foreach (var g in logType.GetGenericArguments())
        Console.WriteLine($"  generic arg: {g.FullName}");
if (logType is not null)
    foreach (var m in logType.GetMethods().Where(m => m.Name == "Invoke"))
        Console.WriteLine($"  Invoke({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
