// Managers/Scheduler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// (CORREÇÃO) Removida a palavra-chave "file". Agora é uma classe interna padrão.
internal class ScheduledTask
{
    public Guid Id { get; } = Guid.NewGuid();
    public Action TaskAction { get; }
    public DateTime ExecutionTime { get; }

    public ScheduledTask(Action action, DateTime executionTime)
    {
        TaskAction = action;
        ExecutionTime = executionTime;
    }
}

/// <summary>
/// Gerencia a execução de ações (tasks) em momentos futuros específicos.
/// Perfeito para buffs que expiram, cooldowns de respawn, efeitos no chão, etc.
/// </summary>
public class Scheduler
{
    private readonly UDPServer _server;
    private readonly List<ScheduledTask> _tasks = new List<ScheduledTask>();
    private readonly object _taskLock = new object();

    public Scheduler(UDPServer server)
    {
        _server = server;
    }

    /// <summary>
    /// Agenda uma ação para ser executada após um determinado atraso.
    /// </summary>
    public void ScheduleTask(Action action, TimeSpan delay)
    {
        DateTime executionTime = _server.CurrentTimeUtc + delay;
        var scheduledTask = new ScheduledTask(action, executionTime);

        lock (_taskLock)
        {
            _tasks.Add(scheduledTask);
        }
    }

    /// <summary>
    /// O loop principal do agendador. Deve ser executado como uma tarefa de fundo.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            List<ScheduledTask> tasksToRun;
            lock (_taskLock)
            {
                tasksToRun = _tasks.Where(t => t.ExecutionTime <= _server.CurrentTimeUtc).ToList();

                if (tasksToRun.Any())
                {
                    _tasks.RemoveAll(t => tasksToRun.Contains(t));
                }
            }

            foreach (var task in tasksToRun)
            {
                try
                {
                    task.TaskAction.Invoke();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SCHEDULER-ERROR] Erro ao executar tarefa agendada: {ex.Message}");
                }
            }

            try
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
        Console.WriteLine("[Scheduler] Agendador de tarefas encerrado.");
    }
}