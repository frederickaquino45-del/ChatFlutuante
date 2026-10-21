using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindowFloatIA.Agents;
namespace WindowFloatIA.Tools;

public static class ToolCatalog
{
    public static readonly Dictionary<string, string> Labels = new() {
        ["create_calendar_event"] = "Criar evento no calendário", ["list_calendar_events"] = "Consultar calendário",
        ["create_alarm"] = "Cadastrar alarme no Relógio", ["open_application"] = "Abrir aplicativo cadastrado", ["get_system_info"] = "Consultar informações do sistema" };
    public static string[] Fields(string name) => name switch {
        "create_calendar_event" => new[] { "title", "start", "end", "time_zone", "description" },
        "list_calendar_events" => new[] { "start", "end" }, "create_alarm" => new[] { "title", "time" },
        "open_application" => new[] { "application_id" }, _ => Array.Empty<string>() };
    public static object Definition(string name, ToolSettings settings)
    {
        var props = Fields(name).ToDictionary(x => x, x => (object)new { type = "string", description = x switch {
            "start" or "end" => "ISO 8601 com offset explícito. Pergunte ao usuário se faltar data ou horário.",
            "time_zone" => "Fuso IANA ou Windows compatível com os offsets.", "time" => "Horário local HH:mm, sem repetição.",
            "application_id" => "Um destes IDs cadastrados: " + string.Join(", ", settings.Applications.Select(a => a.Id)), _ => x } });
        return new { type = "function", function = new { name, description = Labels[name], parameters = new { type = "object", properties = props, required = Fields(name).Where(x => x != "description").ToArray(), additionalProperties = false } } };
    }
    public static string Required(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 4000) throw new AgentException("Parâmetro ausente ou inválido: " + name + ". Peça esclarecimento ao usuário.");
        return value.GetString()!;
    }
    public static (DateTimeOffset Start, DateTimeOffset End) Interval(JsonElement args)
    {
        DateTimeOffset Parse(string key)
        {
            var s = Required(args, key);
            if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"T.*(Z|[+-]\d{2}:\d{2})$") || !DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var result)) throw new AgentException("Informe data/hora ISO 8601 com fuso explícito.");
            return result;
        }
        var start = Parse("start"); var end = Parse("end");
        if (end <= start || end - start > TimeSpan.FromDays(366)) throw new AgentException("Intervalo inválido ou superior a um ano.");
        return (start, end);
    }
    public static void Validate(string name, JsonElement args)
    {
        if (!Labels.ContainsKey(name) || args.ValueKind != JsonValueKind.Object) throw new AgentException("Ferramenta ou argumentos inválidos.");
        foreach (var p in args.EnumerateObject()) if (!Fields(name).Contains(p.Name)) throw new AgentException("Parâmetro não permitido: " + p.Name);
        foreach (var f in Fields(name).Where(x => x != "description")) Required(args, f);
        if (name is "create_calendar_event" or "list_calendar_events") Interval(args);
        if (name == "create_calendar_event")
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(Required(args, "time_zone")); var interval = Interval(args);
            if (zone.GetUtcOffset(interval.Start) != interval.Start.Offset || zone.GetUtcOffset(interval.End) != interval.End.Offset) throw new AgentException("Fuso e horários não correspondem. Peça esclarecimento ao usuário.");
            if (args.TryGetProperty("description", out _)) Required(args, "description");
        }
        if (name == "create_alarm" && !TimeOnly.TryParseExact(Required(args, "time"), "HH:mm", out _)) throw new AgentException("Horário inválido; use HH:mm.");
    }
    public static string? Unavailable(string name, ToolSettings settings) => name switch {
        "create_calendar_event" or "list_calendar_events" => settings.Calendars.Any(a => a.Id == settings.DefaultCalendarAccount && a.CalendarId.Length > 0 && (a.RefreshToken.Length > 0 || a.Expires > DateTimeOffset.UtcNow)) ? null : "Conecte uma conta e selecione o calendário padrão.",
        "open_application" => settings.Applications.Count > 0 ? null : "Cadastre pelo menos um aplicativo.",
        "create_alarm" => ClockAutomation.UnavailableReason,
        _ => null };
}
public sealed class ToolExecutor(ToolSettingsStore store, System.Net.Http.HttpClient http)
{
    public async Task<object> Execute(string name, JsonElement args, string operationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); ToolCatalog.Validate(name, args);
        void Record(string status)
        {
            var latest = store.Load(); latest.Operations.RemoveAll(o => o.Id == operationId);
            latest.Operations.Add(new(operationId, name, status, DateTimeOffset.UtcNow));
            latest.Operations = latest.Operations.TakeLast(100).ToList(); store.Save(latest);
        }
        Record("Executando");
        try { var result = await ExecuteCore(name, args, operationId, ct); Record("Concluída"); return result; }
        catch { Record("Resultado inconclusivo — conferir antes de repetir"); throw; }
    }
    private async Task<object> ExecuteCore(string name, JsonElement args, string operationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); ToolCatalog.Validate(name, args);
        var settings = store.Load(); var unavailable = ToolCatalog.Unavailable(name, settings);
        if (unavailable != null) throw new AgentException(unavailable);
        switch (name)
        {
            case "open_application":
                var app = settings.Applications.SingleOrDefault(a => a.Id == ToolCatalog.Required(args, "application_id")) ?? throw new AgentException("Aplicativo não cadastrado.");
                ValidateApplication(app.Path);
                ct.ThrowIfCancellationRequested();
                Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = false });
                return new { status = "opened", application = app.Id };
            case "get_system_info": return SystemInformation();
            case "create_alarm": return await ClockAutomation.Create(ToolCatalog.Required(args, "title"), ToolCatalog.Required(args, "time"), ct);
            default:
                var account = settings.Calendars.Single(a => a.Id == settings.DefaultCalendarAccount);
                var calendar = new CalendarService(http, () => {
                    var latest = store.Load(); var target = latest.Calendars.FirstOrDefault(a => a.Id == account.Id);
                    if (target != null) { target.AccessToken = account.AccessToken; target.RefreshToken = account.RefreshToken; target.Expires = account.Expires; store.Save(latest); }
                });
                if (name == "create_calendar_event") return await calendar.Create(account, args, operationId, ct);
                var interval = ToolCatalog.Interval(args); return await calendar.ListEvents(account, interval.Start, interval.End, ct);
        }
    }
    public static void ValidateApplication(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") || !File.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) throw new AgentException("Selecione um executável local existente.");
        var blocked = new[] { "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32" };
        if (blocked.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)) throw new AgentException("Interpretadores de comandos não são permitidos.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Memory { public uint Length, Load; public ulong Total, Available, PageTotal, PageAvailable, VirtualTotal, VirtualAvailable, Extended; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref Memory memory);
    private static object SystemInformation()
    {
        var memory = new Memory { Length = (uint)Marshal.SizeOf<Memory>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new AgentException("Não foi possível consultar a memória.");
        using var cpu = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return new { windows = Environment.OSVersion.VersionString, architecture = RuntimeInformation.OSArchitecture.ToString(), processor = cpu?.GetValue("ProcessorNameString")?.ToString()?.Trim(), logical_processors = Environment.ProcessorCount, memory_total_bytes = memory.Total, memory_available_bytes = memory.Available };
    }
}
