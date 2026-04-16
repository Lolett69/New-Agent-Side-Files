using Microsoft.AspNetCore.Mvc;
using NextHorizon.Models;
using NextHorizon.Filters;
using Microsoft.EntityFrameworkCore;
using System;

namespace NextHorizon.Controllers
{
    [AuthenticationFilter]
    public class AgentController : Controller
    {
        private readonly AppDbContext _context;

        public AgentController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult HelpCenter()
        {
            var fullName = HttpContext.Session.GetString("FullName");
            var username = HttpContext.Session.GetString("Username");
            ViewBag.AgentName = !string.IsNullOrWhiteSpace(fullName) ? fullName : (!string.IsNullOrWhiteSpace(username) ? username : "Agent");
            ViewBag.AgentUsername = HttpContext.Session.GetString("Username") ?? "";
            ViewBag.UserId = HttpContext.Session.GetInt32("UserId") ?? 0;
            return View();
        }

        public async Task<IActionResult> FAQs()
        {
            var faqs = await _context.FAQs.ToListAsync();
            return View(faqs);
        }

        public IActionResult CreateFAQ() => View();

        [HttpPost]
        public async Task<IActionResult> CreateFAQ(FAQ faq)
        {
            if (ModelState.IsValid)
            {
                faq.DateAdded = DateTime.Now;
                faq.LastUpdated = DateTime.Now;
                _context.FAQs.Add(faq);
                await _context.SaveChangesAsync();
                return RedirectToAction("FAQs");
            }
            return View(faq);
        }

        public async Task<IActionResult> EditFAQ(int id)
        {
            var faq = await _context.FAQs.FindAsync(id);
            if (faq == null) return NotFound();
            return View(faq);
        }

        [HttpPost]
        public async Task<IActionResult> EditFAQ(FAQ faq)
        {
            if (ModelState.IsValid)
            {
                faq.LastUpdated = DateTime.Now;
                _context.FAQs.Update(faq);
                await _context.SaveChangesAsync();
                return RedirectToAction("FAQs");
            }
            return View(faq);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteFAQ(int id)
        {
            var faq = await _context.FAQs.FindAsync(id);
            if (faq != null)
            {
                _context.FAQs.Remove(faq);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("FAQs");
        }

        // ── CONVERSATIONS ─────────────────────────────────────────
        // SupportFAQs is the real conversation table.
        // SupportMessages.ConversationId → SupportFAQs.Id

        [HttpGet]
        public async Task<IActionResult> GetConversations()
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            if (userId == 0)
                return Json(new { success = false, message = "Not logged in." });

            // Pull conversations assigned to this agent OR unassigned (agent can claim)
            var conversations = await _context.SupportFAQs
                .Where(f => f.AgentId == userId || f.AgentId == null)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new {
                    id = f.Id,
                    category = f.Category,
                    question = f.Question,
                    status = f.Status,
                    userType = f.UserType,
                    agentId = f.AgentId,
                    createdAt = f.CreatedAt,
                    startTime = f.StartTime,
                    endTime = f.EndTime,
                    isAssigned = f.AgentId == userId
                })
                .ToListAsync();

            return Json(new { success = true, conversations });
        }

        [HttpGet]
        public async Task<IActionResult> GetMessages(int conversationId)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            if (userId == 0)
                return Json(new { success = false, message = "Not logged in." });

            // Allow access if assigned to this agent OR unassigned
            var conversation = await _context.SupportFAQs
                .FirstOrDefaultAsync(f => f.Id == conversationId &&
                    (f.AgentId == userId || f.AgentId == null));

            if (conversation == null)
                return Json(new { success = false, message = "Conversation not found or not assigned to you." });

            var messages = await _context.SupportMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new {
                    m.Id,
                    m.ConversationId,
                    m.SenderId,
                    m.SenderRole,
                    m.MessageText,
                    m.CreatedAt
                })
                .ToListAsync();

            return Json(new { success = true, messages });
        }

        // Claim an unassigned conversation
        [HttpPost]
        public async Task<IActionResult> ClaimConversation([FromBody] ClaimConversationRequest model)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            if (userId == 0)
                return Json(new { success = false, message = "Not logged in." });

            if (model == null || model.ConversationId <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            var conversation = await _context.SupportFAQs
                .FirstOrDefaultAsync(f => f.Id == model.ConversationId && f.AgentId == null);

            if (conversation == null)
                return Json(new { success = false, message = "Conversation not found or already claimed." });

            conversation.AgentId = userId;
            conversation.StartTime = DateTime.Now;
            await _context.SaveChangesAsync();
            await SetConversationChatStatusAsync(userId, HttpContext.Session.GetString("FullName") ?? string.Empty, model.ConversationId, "In Chat", null, null);

            return Json(new { success = true, conversationId = model.ConversationId });
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest model)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            if (userId == 0)
                return Json(new { success = false, message = "Not logged in." });

            if (model == null || model.ConversationId <= 0 || string.IsNullOrWhiteSpace(model.MessageText))
                return BadRequest(new { success = false, message = "Invalid request." });

            // Allow send only if assigned to this agent
            var conversation = await _context.SupportFAQs
                .FirstOrDefaultAsync(f => f.Id == model.ConversationId &&
                    f.AgentId == userId);

            if (conversation == null)
                return Json(new { success = false, message = "Conversation not found or not assigned to you." });

            if (string.Equals(conversation.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Conversation is resolved. Unresolve it first to send a message." });

            var message = new SupportMessage
            {
                ConversationId = model.ConversationId,
                SenderId = userId,
                SenderRole = "Agent",
                MessageText = model.MessageText,
                CreatedAt = DateTime.Now
            };

            _context.SupportMessages.Add(message);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                id = message.Id,
                conversationId = message.ConversationId,
                senderId = message.SenderId,
                senderRole = message.SenderRole,
                messageText = message.MessageText,
                createdAt = message.CreatedAt
            });
        }

        // Resolve a conversation
        [HttpPost]
        public async Task<IActionResult> ResolveConversation([FromBody] ClaimConversationRequest model)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            if (userId == 0)
                return Json(new { success = false, message = "Not logged in." });

            var conversation = await _context.SupportFAQs
                .FirstOrDefaultAsync(f => f.Id == model.ConversationId && f.AgentId == userId);

            if (conversation == null)
                return Json(new { success = false, message = "Conversation not found." });

            conversation.Status = "Resolved";
            conversation.EndTime = DateTime.Now;
            await _context.SaveChangesAsync();
            await SetConversationChatStatusAsync(userId, HttpContext.Session.GetString("FullName") ?? string.Empty, model.ConversationId, "ACW", null, null);

            return Json(new { success = true, conversationId = model.ConversationId });
        }

        [HttpPost]
        public async Task<IActionResult> EndConversation([FromBody] ClaimConversationRequest model)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            if (userId == 0)
                return Json(new { success = false, message = "Not logged in." });

            if (model == null || model.ConversationId <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            var conversation = await _context.SupportFAQs
                .FirstOrDefaultAsync(f => f.Id == model.ConversationId && f.AgentId == userId);

            if (conversation == null)
                return Json(new { success = false, message = "Conversation not found." });

            // Persist end-of-conversation state in existing SupportFAQs columns
            conversation.Status = "Resolved";
            conversation.EndTime = DateTime.Now;
            await _context.SaveChangesAsync();
            await SetConversationChatStatusAsync(userId, HttpContext.Session.GetString("FullName") ?? string.Empty, model.ConversationId, "ACW", null, null);

            return Json(new { success = true, conversationId = model.ConversationId });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateConversationStatus([FromBody] UpdateConversationStatusRequest model)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            if (userId == 0)
                return Json(new { success = false, message = "Not logged in." });

            if (model == null || model.ConversationId <= 0 || string.IsNullOrWhiteSpace(model.Status))
                return BadRequest(new { success = false, message = "Invalid request." });

            var conversation = await _context.SupportFAQs
                .FirstOrDefaultAsync(f => f.Id == model.ConversationId && f.AgentId == userId);

            if (conversation == null)
                return Json(new { success = false, message = "Conversation not found." });

            var normalizedStatus = string.Equals(model.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
                ? "Resolved"
                : "Active";

            if (string.Equals(conversation.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedStatus, "Resolved", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Resolved conversations cannot be un-resolved." });
            }

            conversation.Status = normalizedStatus;
            conversation.EndTime = normalizedStatus == "Resolved" ? DateTime.Now : null;
            await _context.SaveChangesAsync();

            var chatStatus = normalizedStatus == "Resolved" ? "ACW" : "In Chat";
            await SetConversationChatStatusAsync(userId, HttpContext.Session.GetString("FullName") ?? string.Empty, model.ConversationId, chatStatus, null, null);

            return Json(new { success = true, conversationId = model.ConversationId, status = normalizedStatus });
        }

        // ── AGENT STATUS ──────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetAgentStatus(string agentName)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            var resolvedAgentName = ResolveAgentName(agentName);

            var agent = await _context.Agents
                .Where(a => (userId != 0 && a.AgentID == userId) || a.AgentName == resolvedAgentName)
                .OrderByDescending(a => a.ChatID)
                .FirstOrDefaultAsync();

            var status = ToUiAgentStatus(agent?.AgentStatus ?? "Available");
            return Json(new { success = true, status = status });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateAgentStatus([FromBody] UpdateAgentStatusRequest model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Status))
                return BadRequest(new { success = false, message = "Invalid request." });

            var userId = HttpContext.Session.GetInt32("UserId") ?? model.AgentUserId;
            var resolvedAgentName = ResolveAgentName(model.AgentName);
            userId = await ResolveAgentUserIdAsync(userId, resolvedAgentName);

            if (!IsUsableAgentName(resolvedAgentName) && userId == 0)
                return BadRequest(new { success = false, message = "Agent identity is required." });

            if (model.IsChatStatus)
            {
                if (model.ConversationId <= 0)
                    return BadRequest(new { success = false, message = "Conversation is required for chat status updates." });

                var normalizedChatStatus = NormalizeChatStatus(model.Status);
                try
                {
                    await SetConversationChatStatusAsync(userId, resolvedAgentName, model.ConversationId, normalizedChatStatus, model.ChatSlot, null);
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Failed to update chat status.", detail = ex.Message });
                }
                return Json(new { success = true, status = normalizedChatStatus });
            }

            var normalizedAgentStatus = NormalizeAgentStatus(model.Status);
            try
            {
                await UpdateAllChatSlotAgentStatusAsync(resolvedAgentName, userId, normalizedAgentStatus);
                await UpsertAgentStatusRowsAsync(userId, resolvedAgentName, normalizedAgentStatus);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to update agent status.", detail = ex.Message });
            }

            return Json(new { success = true, status = ToUiAgentStatus(normalizedAgentStatus) });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateAgentStatusDirect(string status, int agentUserId, string agentName)
        {
            if (string.IsNullOrWhiteSpace(status))
                return BadRequest(new { success = false, message = "Invalid request." });

            var normalizedAgentStatus = NormalizeAgentStatus(status);
            var resolvedAgentName = ResolveAgentName(agentName);
            var userId = await ResolveAgentUserIdAsync(agentUserId, resolvedAgentName);

            try
            {
                await UpdateAllChatSlotAgentStatusAsync(resolvedAgentName, userId, normalizedAgentStatus);
                await UpsertAgentStatusRowsAsync(userId, resolvedAgentName, normalizedAgentStatus);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to update agent status directly.", detail = ex.Message });
            }

            return Json(new { success = true, status = ToUiAgentStatus(normalizedAgentStatus) });
        }

        [HttpPost]
        public async Task<IActionResult> SaveConversationNotes([FromBody] SaveConversationNotesRequest model)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? model.AgentUserId;
            if (userId == 0)
                return Json(new { success = false, message = "Not logged in." });

            if (model == null || model.ConversationId <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            var agentName = ResolveAgentName(model.AgentName);
            userId = await ResolveAgentUserIdAsync(userId, agentName);

            await UpdateAgentNotesAsync(userId, agentName, model.ConversationId, model.ChatSlot, model.Notes ?? string.Empty);
            return Json(new { success = true });
        }

        private string ResolveAgentName(string requestedAgentName)
        {
            var sessionName = HttpContext.Session.GetString("FullName") ?? string.Empty;
            if (IsUsableAgentName(sessionName))
            {
                return sessionName;
            }

            if (IsUsableAgentName(requestedAgentName))
            {
                return requestedAgentName;
            }

            var username = HttpContext.Session.GetString("Username") ?? string.Empty;
            if (IsUsableAgentName(username))
            {
                return username;
            }

            return sessionName;
        }

        private static bool IsUsableAgentName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return !string.Equals(value.Trim(), "Agent", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAgentStatus(string status)
        {
            var normalized = status?.Trim().ToLowerInvariant() ?? "available";
            return normalized switch
            {
                "available" => "Available",
                "break" => "Break",
                "lunch" => "Lunch",
                "eos" => "EOS",
                _ => "Available"
            };
        }

        private static string ToUiAgentStatus(string status)
        {
            var normalized = status?.Trim().ToLowerInvariant() ?? "available";
            return normalized switch
            {
                "available" => "available",
                "break" => "break",
                "lunch" => "lunch",
                "eos" => "eos",
                _ => "available"
            };
        }

        private static string NormalizeChatStatus(string status)
        {
            var normalized = status?.Trim().ToLowerInvariant() ?? "available";
            return normalized switch
            {
                "inchat" => "In Chat",
                "available" => "Available",
                "break" => "Break",
                "lunch" => "Lunch",
                "eos" => "EOS",
                "acw" => "ACW",
                _ => "Available"
            };
        }

        private static string NormalizeAgentsTableChatStatus(string chatStatus)
        {
            var normalized = chatStatus?.Trim().ToLowerInvariant() ?? "active";
            return normalized switch
            {
                "acw" => "Resolved",
                "resolved" => "Resolved",
                _ => "Active"
            };
        }

        private async Task SetConversationChatStatusAsync(int userId, string agentName, int conversationId, string chatStatus, int? chatSlot, string? notes)
        {
            var resolvedAgentName = !string.IsNullOrWhiteSpace(agentName)
                ? agentName
                : (HttpContext.Session.GetString("FullName") ?? string.Empty);
            var persistedAgentChatStatus = NormalizeAgentsTableChatStatus(chatStatus);

            var rows = await _context.Agents
                .Where(a => a.ConversationID == conversationId)
                .OrderByDescending(a => a.ChatID)
                .ToListAsync();

            if (rows.Count == 0 && !string.IsNullOrWhiteSpace(resolvedAgentName))
            {
                var fallback = await _context.Agents
                    .Where(a => ((userId != 0 && a.AgentID == userId) || a.AgentName == resolvedAgentName)
                        && (!chatSlot.HasValue || a.ChatSlot == chatSlot.Value))
                    .OrderByDescending(a => a.ChatID)
                    .Take(1)
                    .ToListAsync();

                rows.AddRange(fallback);
            }

            if (rows.Count == 0)
            {
                var fallbackAgentName = ResolvePersistedAgentName(resolvedAgentName, userId);
                if (!string.IsNullOrWhiteSpace(fallbackAgentName))
                {
                    var conversation = await _context.SupportFAQs
                        .AsNoTracking()
                        .FirstOrDefaultAsync(f => f.Id == conversationId);

                    var latestAgentStatus = await _context.Agents
                        .Where(a => (userId != 0 && a.AgentID == userId) || a.AgentName == fallbackAgentName)
                        .OrderByDescending(a => a.ChatID)
                        .Select(a => a.AgentStatus)
                        .FirstOrDefaultAsync();

                    var createdRow = new Agent
                    {
                        AgentID = userId > 0 ? userId : null,
                        ConversationID = conversationId,
                        AgentName = fallbackAgentName,
                        ClientName = conversation?.UserType ?? "N/A",
                        Category = conversation?.Category ?? "N/A",
                        PreviewQuestion = conversation?.Question ?? "Status update",
                        ChatSlot = chatSlot.HasValue && chatSlot.Value >= 1 && chatSlot.Value <= 3 ? chatSlot.Value : 1,
                        ChatStatus = persistedAgentChatStatus,
                        AgentStatus = string.IsNullOrWhiteSpace(latestAgentStatus) ? "Available" : latestAgentStatus
                    };

                    _context.Agents.Add(createdRow);
                    await _context.SaveChangesAsync();
                    rows.Add(createdRow);
                }
            }

            var shouldSave = false;
            foreach (var row in rows)
            {
                if (!string.Equals(row.ChatStatus, persistedAgentChatStatus, StringComparison.OrdinalIgnoreCase))
                {
                    row.ChatStatus = persistedAgentChatStatus;
                    shouldSave = true;
                }

                if (row.ConversationID != conversationId)
                {
                    row.ConversationID = conversationId;
                    shouldSave = true;
                }

                if (chatSlot.HasValue && chatSlot.Value >= 1 && chatSlot.Value <= 3 && row.ChatSlot != chatSlot.Value)
                {
                    row.ChatSlot = chatSlot.Value;
                    shouldSave = true;
                }
            }

            if (shouldSave)
            {
                await _context.SaveChangesAsync();
            }

            var targetSlot = chatSlot;
            if ((!targetSlot.HasValue || targetSlot.Value < 1 || targetSlot.Value > 3) && rows.Count > 0)
            {
                targetSlot = rows[0].ChatSlot;
            }

            if (targetSlot.HasValue && targetSlot.Value >= 1 && targetSlot.Value <= 3)
            {
                await UpdateChatSlotStatusAsync(targetSlot.Value, resolvedAgentName, userId, chatStatus);
                if (notes != null)
                {
                    await UpdateChatSlotNotesAsync(targetSlot.Value, resolvedAgentName ?? string.Empty, userId, notes ?? string.Empty);
                }
            }
        }

        private async Task UpdateAllChatSlotAgentStatusAsync(string agentName, int userId, string agentStatus)
        {
            for (var slot = 1; slot <= 3; slot++)
            {
                var tableName = $"dbo.ChatSlot_{slot}";
                var sql = $@"
IF OBJECT_ID('{tableName}', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('{tableName}', 'LastUpdatedAt') IS NOT NULL
    BEGIN
        UPDATE {tableName}
        SET AgentStatus = {{0}},
            LastUpdatedAt = GETDATE()
        WHERE ({{2}} > 0 AND AgentId = {{2}})
           OR ({{2}} <= 0 AND (
                LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({{1}})))
                OR LOWER(ISNULL(AgentName, '')) LIKE '%' + LOWER(LTRIM(RTRIM({{1}}))) + '%'
           ));
    END
    ELSE
    BEGIN
        UPDATE {tableName}
        SET AgentStatus = {{0}}
        WHERE ({{2}} > 0 AND AgentId = {{2}})
           OR ({{2}} <= 0 AND (
                LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({{1}})))
                OR LOWER(ISNULL(AgentName, '')) LIKE '%' + LOWER(LTRIM(RTRIM({{1}}))) + '%'
           ));
    END

    IF COL_LENGTH('{tableName}', 'AgentStatusLastUpdatedAt') IS NOT NULL
    BEGIN
        UPDATE {tableName}
        SET AgentStatusLastUpdatedAt = GETDATE()
        WHERE ({{2}} > 0 AND AgentId = {{2}})
           OR ({{2}} <= 0 AND (
                LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({{1}})))
                OR LOWER(ISNULL(AgentName, '')) LIKE '%' + LOWER(LTRIM(RTRIM({{1}}))) + '%'
           ));
    END
END";

                await _context.Database.ExecuteSqlRawAsync(sql, agentStatus, agentName, userId);
            }
        }

        private async Task UpdateChatSlotStatusAsync(int slot, string agentName, int userId, string chatStatus)
        {
            if (slot < 1 || slot > 3)
                return;

            var tableName = $"dbo.ChatSlot_{slot}";
            var sql = $@"
IF OBJECT_ID('{tableName}', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('{tableName}', 'LastUpdatedAt') IS NOT NULL
    BEGIN
        UPDATE {tableName}
        SET ChatStatus = {{0}},
            LastUpdatedAt = GETDATE()
        WHERE ({{2}} > 0 AND AgentId = {{2}})
           OR ({{2}} <= 0 AND (
                LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({{1}})))
                OR LOWER(ISNULL(AgentName, '')) LIKE '%' + LOWER(LTRIM(RTRIM({{1}}))) + '%'
           ));
    END
    ELSE
    BEGIN
        UPDATE {tableName}
        SET ChatStatus = {{0}}
        WHERE ({{2}} > 0 AND AgentId = {{2}})
           OR ({{2}} <= 0 AND (
                LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({{1}})))
                OR LOWER(ISNULL(AgentName, '')) LIKE '%' + LOWER(LTRIM(RTRIM({{1}}))) + '%'
           ));
    END

    IF COL_LENGTH('{tableName}', 'ChatStatusLastUpdatedAt') IS NOT NULL
    BEGIN
        UPDATE {tableName}
        SET ChatStatusLastUpdatedAt = GETDATE()
        WHERE ({{2}} > 0 AND AgentId = {{2}})
           OR ({{2}} <= 0 AND (
                LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({{1}})))
                OR LOWER(ISNULL(AgentName, '')) LIKE '%' + LOWER(LTRIM(RTRIM({{1}}))) + '%'
           ));
    END
END";

            await _context.Database.ExecuteSqlRawAsync(sql, chatStatus, agentName ?? string.Empty, userId);
        }

        private async Task UpdateChatSlotNotesAsync(int slot, string agentName, int userId, string notes)
        {
            if (slot < 1 || slot > 3)
                return;

            var tableName = $"dbo.ChatSlot_{slot}";
            var sql = $@"
IF OBJECT_ID('{tableName}', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('{tableName}', 'Notes') IS NOT NULL
    BEGIN
        IF COL_LENGTH('{tableName}', 'LastUpdatedAt') IS NOT NULL
        BEGIN
            UPDATE {tableName}
            SET Notes = {{0}},
                LastUpdatedAt = GETDATE()
            WHERE ({{2}} > 0 AND AgentId = {{2}})
               OR ({{2}} <= 0 AND (
                    LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({{1}})))
                    OR LOWER(ISNULL(AgentName, '')) LIKE '%' + LOWER(LTRIM(RTRIM({{1}}))) + '%'
               ));
        END
        ELSE
        BEGIN
            UPDATE {tableName}
            SET Notes = {{0}}
            WHERE ({{2}} > 0 AND AgentId = {{2}})
               OR ({{2}} <= 0 AND (
                    LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({{1}})))
                    OR LOWER(ISNULL(AgentName, '')) LIKE '%' + LOWER(LTRIM(RTRIM({{1}}))) + '%'
               ));
        END
    END

    IF COL_LENGTH('{tableName}', 'NotesLastUpdatedAt') IS NOT NULL
    BEGIN
        UPDATE {tableName}
        SET NotesLastUpdatedAt = GETDATE()
        WHERE ({{2}} > 0 AND AgentId = {{2}})
           OR ({{2}} <= 0 AND (
                LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({{1}})))
                OR LOWER(ISNULL(AgentName, '')) LIKE '%' + LOWER(LTRIM(RTRIM({{1}}))) + '%'
           ));
    END
END";

            await _context.Database.ExecuteSqlRawAsync(sql, notes ?? string.Empty, agentName ?? string.Empty, userId);
        }

        private async Task UpdateAgentNotesAsync(int userId, string agentName, int conversationId, int? chatSlot, string notes)
        {
            var normalizedSlot = (chatSlot.HasValue && chatSlot.Value >= 1 && chatSlot.Value <= 3)
                ? chatSlot.Value
                : (int?)null;

            var agentSql = @"
IF OBJECT_ID('dbo.Agents', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Agents', 'Notes') IS NOT NULL
    BEGIN
        UPDATE dbo.Agents
        SET Notes = {0}
        WHERE ConversationID = {1}
           OR ({3} > 0 AND AgentID = {3} AND ({4} = 0 OR ChatSlot = {4}))
           OR ({3} <= 0 AND LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({2}))) AND ({4} = 0 OR ChatSlot = {4}));
    END

    IF COL_LENGTH('dbo.Agents', 'NotesLastUpdatedAt') IS NOT NULL
    BEGIN
        UPDATE dbo.Agents
        SET NotesLastUpdatedAt = GETDATE()
        WHERE ConversationID = {1}
           OR ({3} > 0 AND AgentID = {3} AND ({4} = 0 OR ChatSlot = {4}))
           OR ({3} <= 0 AND LOWER(LTRIM(RTRIM(ISNULL(AgentName, '')))) = LOWER(LTRIM(RTRIM({2}))) AND ({4} = 0 OR ChatSlot = {4}));
    END
END";

            await _context.Database.ExecuteSqlRawAsync(
                agentSql,
                notes ?? string.Empty,
                conversationId,
                agentName ?? string.Empty,
                userId,
                normalizedSlot ?? 0);

            if (normalizedSlot.HasValue)
            {
                await UpdateChatSlotNotesAsync(normalizedSlot.Value, agentName ?? string.Empty, userId, notes ?? string.Empty);
            }
        }

        private async Task<int> ResolveAgentUserIdAsync(int userId, string agentName)
        {
            if (userId > 0)
                return userId;

            if (!IsUsableAgentName(agentName))
                return 0;

            var candidate = await _context.Agents
                .Where(a => a.AgentName == agentName && a.AgentID.HasValue)
                .OrderByDescending(a => a.ChatID)
                .Select(a => a.AgentID)
                .FirstOrDefaultAsync();

            return candidate ?? 0;
        }

        private async Task UpsertAgentStatusRowsAsync(int userId, string agentName, string agentStatus)
        {
            var persistedAgentName = ResolvePersistedAgentName(agentName, userId);
            var hasUsableAgentName = IsUsableAgentName(persistedAgentName);

            var agentRows = await _context.Agents
                .Where(a => (userId != 0 && a.AgentID == userId)
                    || (hasUsableAgentName && a.AgentName == persistedAgentName))
                .ToListAsync();

            if (agentRows.Count > 0)
            {
                foreach (var row in agentRows)
                {
                    row.AgentStatus = agentStatus;
                }

                await _context.SaveChangesAsync();
                return;
            }

            var latestAgent = await _context.Agents
                .Where(a => (userId != 0 && a.AgentID == userId)
                    || (hasUsableAgentName && a.AgentName == persistedAgentName))
                .OrderByDescending(a => a.ChatID)
                .FirstOrDefaultAsync();

            var newRow = new Agent
            {
                AgentID = userId > 0 ? userId : null,
                AgentName = hasUsableAgentName ? persistedAgentName : $"Agent {userId}",
                ClientName = latestAgent?.ClientName ?? "N/A",
                Category = latestAgent?.Category ?? "N/A",
                PreviewQuestion = latestAgent?.PreviewQuestion ?? "Status update",
                ChatSlot = latestAgent?.ChatSlot ?? 1,
                ChatStatus = latestAgent?.ChatStatus ?? "Available",
                AgentStatus = agentStatus
            };

            _context.Agents.Add(newRow);
            await _context.SaveChangesAsync();
        }

        private string ResolvePersistedAgentName(string requestedAgentName, int userId)
        {
            if (IsUsableAgentName(requestedAgentName))
            {
                return requestedAgentName.Trim();
            }

            var sessionFullName = HttpContext.Session.GetString("FullName") ?? string.Empty;
            if (IsUsableAgentName(sessionFullName))
            {
                return sessionFullName.Trim();
            }

            var username = HttpContext.Session.GetString("Username") ?? string.Empty;
            if (IsUsableAgentName(username))
            {
                return username.Trim();
            }

            return userId > 0 ? $"Agent {userId}" : "Agent";
        }

    }

    public class UpdateAgentStatusRequest
    {
        public string AgentName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int ConversationId { get; set; }
        public bool IsChatStatus { get; set; }
        public int? ChatSlot { get; set; }
        public int AgentUserId { get; set; }
    }

    public class SaveConversationNotesRequest
    {
        public int ConversationId { get; set; }
        public string AgentName { get; set; } = string.Empty;
        public int? ChatSlot { get; set; }
        public string Notes { get; set; } = string.Empty;
        public int AgentUserId { get; set; }
    }

    public class SendMessageRequest
    {
        public int ConversationId { get; set; }
        public string MessageText { get; set; } = string.Empty;
    }

    public class ClaimConversationRequest
    {
        public int ConversationId { get; set; }
    }

    public class UpdateConversationStatusRequest
    {
        public int ConversationId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}