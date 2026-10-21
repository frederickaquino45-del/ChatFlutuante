using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using WindowFloatIA.Agents;
namespace WindowFloatIA.Tools;

// Conservative accessibility adapter. Runtime probing is mandatory in each app session.
// Controls must be uniquely identifiable and expose the required UIA patterns.
public static class ClockAutomation
{
    private static bool _verified;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static string? UnavailableReason => _verified ? null : "Valide os controles do Relógio nesta sessão antes de habilitar alarmes.";
    private static AutomationElement[] Descendants(AutomationElement root) => root.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().ToArray();
    private static AutomationElement Unique(AutomationElement root, params string[] labels)
    {
        var found = Descendants(root).Where(e => labels.Any(l => string.Equals(e.Current.AutomationId, l, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Current.Name, l, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (found.Length != 1) throw new AgentException("Esta versão do Relógio não expõe controles únicos compatíveis. Cadastro automático desabilitado.");
        return found[0];
    }
    private static void Invoke(AutomationElement e)
    {
        if (e.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) ((InvokePattern)invoke).Invoke();
        else if (e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection)) ((SelectionItemPattern)selection).Select();
        else throw new AgentException("O controle do Relógio não permite ação por acessibilidade.");
    }
    private static ValuePattern Value(AutomationElement e) => e.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && !((ValuePattern)pattern).Current.IsReadOnly ? (ValuePattern)pattern : throw new AgentException("Campo do Relógio não é editável por acessibilidade.");
    private static AutomationElement Window()
    {
        var candidates = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>()
            .Where(e => e.Current.Name is "Relógio" or "Clock" or "Alarmes e Relógio" or "Alarms & Clock").ToArray();
        if (candidates.Length != 1) throw new AgentException("Abra o Relógio e deixe-o disponível para validar a integração.");
        var window = candidates[0];
        // Validate the actual process identity, not a title supplied by another application.
        using var process = Process.GetProcessById(window.Current.ProcessId);
        var path = process.MainModule?.FileName ?? "";
        if (!path.Contains(@"\WindowsApps\Microsoft.WindowsAlarms_", StringComparison.OrdinalIgnoreCase)) throw new AgentException("Não foi possível verificar a identidade do processo Relógio.");
        return window;
    }
    private static async Task<AutomationElement> Open(CancellationToken ct)
    {
        Process.Start(new ProcessStartInfo("ms-clock:") { UseShellExecute = true });
        for (var i = 0; i < 20; i++)
        {
            ct.ThrowIfCancellationRequested();
            try { return Window(); } catch (AgentException) when (i < 19) { await Task.Delay(250, ct); }
        }
        throw new AgentException("Relógio indisponível.");
    }
    private static async Task<(AutomationElement Window, ValuePattern Name, ValuePattern Hour, ValuePattern Minute, AutomationElement Save, AutomationElement Cancel, TogglePattern Repeat)> Editor(CancellationToken ct)
    {
        if (System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("t")) throw new AgentException("A integração experimental exige formato de hora 24h no Windows.");
        var window = await Open(ct);
        if (Descendants(window).Any(e => e.Current.Name is "Salvar" or "Save" || e.Current.AutomationId == "SaveButton")) throw new AgentException("Feche o editor aberto no Relógio antes de continuar.");
        Invoke(Unique(window, "AlarmPivotItem", "Alarm", "Alarme", "Alarmes", "Alarms"));
        await Task.Delay(300, ct);
        Invoke(Unique(window, "AddAlarmButton", "Adicionar um alarme", "Add an alarm", "Adicionar alarme", "Add alarm"));
        await Task.Delay(300, ct);
        try
        {
            var name = Value(Unique(window, "AlarmNameTextBox", "AlarmName", "Nome do alarme", "Alarm name"));
            var hour = Value(Unique(window, "HourTextBox", "HoursTextBox", "Hour", "Hora", "Horas"));
            var minute = Value(Unique(window, "MinuteTextBox", "MinutesTextBox", "Minute", "Minuto", "Minutos"));
            var save = Unique(window, "SaveButton", "Salvar", "Save"); var cancel = Unique(window, "CancelButton", "Cancelar", "Cancel");
            var repeat = Unique(window, "RepeatCheckBox", "Repetir alarme", "Repeat alarm");
            if (!repeat.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle)) throw new AgentException("Não foi possível verificar a repetição do alarme.");
            if (!save.TryGetCurrentPattern(InvokePattern.Pattern, out _) || !cancel.TryGetCurrentPattern(InvokePattern.Pattern, out _)) throw new AgentException("Botões do Relógio incompatíveis.");
            return (window, name, hour, minute, save, cancel, (TogglePattern)toggle);
        }
        catch { try { Invoke(Unique(window, "CancelButton", "Cancelar", "Cancel")); } catch { } throw; }
    }
    public static async Task Probe(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            _verified = false;
            await Task.Run(async () => { var editor = await Editor(ct); Invoke(editor.Cancel); }, ct);
            _verified = true;
        }
        catch { _verified = false; throw; }
        finally { Gate.Release(); }
    }
    public static async Task<object> Create(string title, string time, CancellationToken ct)
    {
        if (!_verified) throw new AgentException(UnavailableReason!);
        await Gate.WaitAsync(ct);
        try
        {
            return await Task.Run<object>(async () => {
                var editor = await Editor(ct); bool submitted = false;
                try
                {
                    editor.Name.SetValue(title); editor.Hour.SetValue(time[..2]); editor.Minute.SetValue(time[3..]);
                    if (editor.Repeat.Current.ToggleState == ToggleState.On) editor.Repeat.Toggle();
                    if (editor.Repeat.Current.ToggleState != ToggleState.Off) throw new AgentException("Não foi possível desativar a repetição.");
                    ct.ThrowIfCancellationRequested();
                    if (editor.Name.Current.Value != title || !int.TryParse(editor.Hour.Current.Value, out var h) || h != int.Parse(time[..2]) || !int.TryParse(editor.Minute.Current.Value, out var m) || m != int.Parse(time[3..])) throw new AgentException("O Relógio não confirmou os campos preenchidos.");
                    submitted = true; Invoke(editor.Save);
                    await Task.Delay(500, ct);
                    var matches = Descendants(editor.Window).Where(e => e.Current.ControlType == ControlType.ListItem && e.Current.Name.Contains(title, StringComparison.Ordinal) && e.Current.Name.Contains(time, StringComparison.Ordinal)).ToArray();
                    if (matches.Length != 1) throw new AgentException("Resultado inconclusivo: confira o alarme no Relógio antes de repetir.");
                    return new { status = "created", title, time, repeat = false };
                }
                finally { if (!submitted) try { Invoke(editor.Cancel); } catch { } }
            }, ct);
        }
        catch { _verified = false; throw; }
        finally { Gate.Release(); }
    }
}
