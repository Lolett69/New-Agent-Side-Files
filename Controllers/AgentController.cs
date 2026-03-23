using Microsoft.AspNetCore.Mvc;
using NextHorizon.Models;
using Microsoft.EntityFrameworkCore;

namespace NextHorizon.Controllers
{
    public class AgentController : Controller
    {
        private readonly AppDbContext _context;

        public AgentController(AppDbContext context)
        {
            _context = context;
        }

        // Help Center main page
        public IActionResult HelpCenter()
        {
            return View();
        }

        // FAQ List
        public async Task<IActionResult> FAQs()
        {
            var faqs = await _context.FAQs.ToListAsync();
            return View(faqs);
        }

        // FAQ Create
        public IActionResult CreateFAQ()
        {
            return View();
        }

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

        // FAQ Edit
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

        // FAQ Delete
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
    }
}