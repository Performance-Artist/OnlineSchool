﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineSchool.Domain.Contexts;
using OnlineSchool.Domain.Entities;
using OnlineSchool.Presentation.Models.Login;
using MimeKit;
using MailKit.Net.Smtp;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore;


namespace OnlineSchool.Presentation.Controllers
{
    public class LoginController : Controller
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly AppDbContext _appDbContext;

        public LoginController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, AppDbContext appDbContext)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _appDbContext = appDbContext;
        }



        [AllowAnonymous]
        public async Task<IActionResult> Login()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user != null)
            {
                var result = await _signInManager.PasswordSignInAsync(user, model.Password, true, false);

                if (result.IsLockedOut)
                {
                    if (user.LockoutEnd.Value.Year == 5000)
                    {
                        ViewBag.Error = "Your account was blocked, contact the administrator for the additional information";
                    }
                    else
                    {
                        ViewBag.Error = "Too many login attempts, you can try again in 10 minutes";
                    }
                    return View();
                }

                if (result.Succeeded || result.RequiresTwoFactor)
                {
                    string VisitorsIPAddr = "not defined";
                    var httpContext = HttpContext;
                    if (httpContext.Request.Headers.TryGetValue("HTTP_X_FORWARDED_FOR", out var forwardedFor))
                    {
                        VisitorsIPAddr = forwardedFor.FirstOrDefault();
                    }
                    else if (!string.IsNullOrEmpty(httpContext.Connection.RemoteIpAddress?.ToString()))
                    {
                        VisitorsIPAddr = httpContext.Connection.RemoteIpAddress.ToString();
                    }

                    Logins login = new();
                    login.Date = DateTime.Now;
                    login.Email = model.Email;
                    login.Ip = VisitorsIPAddr;
                    await _appDbContext.Logins.AddAsync(login);
                    await _appDbContext.SaveChangesAsync();

                    if (result.RequiresTwoFactor)
                    {
                        var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
                        if (!providers.Contains("Email"))
                        {
                            return BadRequest();
                        }

                        var token = await _userManager.GenerateTwoFactorTokenAsync(user, "Email");

                        await SendEmailAsync(model.Email, "Your Code - ", token);

                        TempData["Email"] = model.Email;
                        return View("LoginSecondStep");
                    }
                    return RedirectToAction("Dashboard", "Home");
                }

                var user2 = await _appDbContext.Users.FirstOrDefaultAsync(x => x.Email == user.Email);
                user2.AccessFailedCount = user2.AccessFailedCount + 1;
                if(user2.AccessFailedCount == 3)
                {
                    user2.AccessFailedCount = 0;
                    user.LockoutEnd = DateTime.Now.AddMinutes(10);
                }
                _appDbContext.Users.Update(user2);
                await _appDbContext.SaveChangesAsync();
                ViewBag.Error = "Wrong credentials";
                return View();
            }
            else
            {
                ViewBag.Error = "Wrong credentials";
                return View();
            }
        }

        [AllowAnonymous]
        public async Task<IActionResult> LoginSecondStep()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> LoginSecondStep(TokenModel tokenMmodel)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                return RedirectToAction(nameof(Error));
            }
            var result = await _signInManager.TwoFactorSignInAsync("Email", tokenMmodel.Token, true, rememberClient: false);
            if (result.Succeeded)
                return RedirectToAction("Dashboard", "Home");
            else
            {
                TempData["Email"] = tokenMmodel.Email;
                ViewBag.Error = "Wrong token please try again";
                return View("LoginSecondStep");
            }
        }




        [Authorize]
        public async Task<IActionResult> LogOut()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Login");
        }

        public async Task SendEmailAsync(string to, string subject, string body)
        {
            var mailMessage = new MimeMessage();
            mailMessage.From.Add(new MailboxAddress("OnlineSchool", "clien7s@yandex.com"));
            mailMessage.To.Add(new MailboxAddress("", to));
            mailMessage.Subject = subject;
            mailMessage.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = body };

            using var mailKitSmtp = new SmtpClient();
            mailKitSmtp.Connect("smtp.yandex.ru", 587, MailKit.Security.SecureSocketOptions.StartTls);
            mailKitSmtp.Authenticate("clien7s@yandex.ru", "oqbptkbucynartaq");
            mailKitSmtp.Send(mailMessage);
            mailKitSmtp.Disconnect(true);
        }
    }
}
