using Microsoft.EntityFrameworkCore;
using NextHorizon.Models;

namespace NextHorizon.Services
{
    public class SupportInactivityMonitorService : BackgroundService
    {
        private const string SystemRole = "System";
        private const int Warn2MinutesSeconds = 120;
        private const int Warn1MinuteSeconds = 240;
        private const int Warn30Seconds = 270;
        private const int AutoResolveSeconds = 300;

        private const string Warn2Message = "⚠ We have not received a reply for 2 minutes. This conversation will close in 3:00 if inactivity continues.";
        private const string Warn1Message = "⚠ Inactivity notice: conversation will close in 1:00 unless we receive your reply.";
        private const string Warn30Message = "⚠ Final reminder: conversation will close in 0:30 unless we receive your reply.";
        private const string AutoEndMessage = "⏱ Conversation ended automatically due to participant inactivity.";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SupportInactivityMonitorService> _logger;

        public SupportInactivityMonitorService(IServiceScopeFactory scopeFactory, ILogger<SupportInactivityMonitorService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessInactivity(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Inactivity monitor loop failed.");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        private async Task ProcessInactivity(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var conversations = await context.SupportFAQs
                .Where(f => f.AgentId != null)
                .ToListAsync(cancellationToken);

            foreach (var conversation in conversations)
            {
                if (string.Equals(conversation.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
                    continue;

                var lastParticipantMessageAt = await context.SupportMessages
                    .Where(m => m.ConversationId == conversation.Id
                        && m.SenderRole != "Agent"
                        && m.SenderRole != SystemRole)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => (DateTime?)m.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (!lastParticipantMessageAt.HasValue)
                    continue;

                var inactiveSeconds = (int)(DateTime.Now - lastParticipantMessageAt.Value).TotalSeconds;
                if (inactiveSeconds < Warn2MinutesSeconds)
                    continue;

                var existingSystemMessages = await context.SupportMessages
                    .Where(m => m.ConversationId == conversation.Id && m.SenderRole == SystemRole)
                    .Select(m => m.MessageText)
                    .ToListAsync(cancellationToken);

                var existingSet = new HashSet<string>(existingSystemMessages);

                if (inactiveSeconds >= AutoResolveSeconds)
                {
                    if (!existingSet.Contains(AutoEndMessage))
                    {
                        context.SupportMessages.Add(new SupportMessage
                        {
                            ConversationId = conversation.Id,
                            SenderId = 0,
                            SenderRole = SystemRole,
                            MessageText = AutoEndMessage,
                            CreatedAt = DateTime.Now
                        });
                    }

                    conversation.Status = "Resolved";
                    conversation.EndTime = DateTime.Now;
                    continue;
                }

                if (inactiveSeconds >= Warn30Seconds)
                {
                    if (!existingSet.Contains(Warn30Message))
                    {
                        context.SupportMessages.Add(new SupportMessage
                        {
                            ConversationId = conversation.Id,
                            SenderId = 0,
                            SenderRole = SystemRole,
                            MessageText = Warn30Message,
                            CreatedAt = DateTime.Now
                        });
                    }

                    continue;
                }

                if (inactiveSeconds >= Warn1MinuteSeconds)
                {
                    if (!existingSet.Contains(Warn1Message))
                    {
                        context.SupportMessages.Add(new SupportMessage
                        {
                            ConversationId = conversation.Id,
                            SenderId = 0,
                            SenderRole = SystemRole,
                            MessageText = Warn1Message,
                            CreatedAt = DateTime.Now
                        });
                    }

                    continue;
                }

                if (!existingSet.Contains(Warn2Message))
                {
                    context.SupportMessages.Add(new SupportMessage
                    {
                        ConversationId = conversation.Id,
                        SenderId = 0,
                        SenderRole = SystemRole,
                        MessageText = Warn2Message,
                        CreatedAt = DateTime.Now
                    });
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
