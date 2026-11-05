using System.Net;

namespace WabiInsight.Core.Email;

/// <summary>Bilingual transactional templates. Arabic messages are rendered RTL.</summary>
public static class EmailTemplates
{
    public static EmailMessage TemporaryPassword(
        string toEmail,
        string toName,
        string temporaryPassword,
        string language,
        bool isNewAccount,
        string signInUrl,
        string replyTo = "")
    {
        var ar = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var name = WebUtility.HtmlEncode(toName);
        var pwd = WebUtility.HtmlEncode(temporaryPassword);
        var url = WebUtility.HtmlEncode(signInUrl);

        string subject, greeting, intro, passwordLabel, instruction, signIn, ignore, footer;
        if (ar)
        {
            subject = isNewAccount ? "حسابك في وابي إنسايت" : "إعادة تعيين كلمة المرور في وابي إنسايت";
            greeting = $"مرحبًا {name}،";
            intro = isNewAccount
                ? "تم إنشاء حساب لك في وابي إنسايت. استخدم كلمة المرور المؤقتة التالية لتسجيل الدخول لأول مرة:"
                : "أعاد المدير تعيين كلمة مرورك في وابي إنسايت. استخدم كلمة المرور المؤقتة التالية لتسجيل الدخول:";
            passwordLabel = "كلمة المرور المؤقتة";
            instruction = "سيُطلب منك تغيير كلمة المرور فور تسجيل الدخول. كلمة المرور المؤقتة صالحة للاستخدام مرة واحدة ولا تشاركها مع أحد.";
            signIn = "تسجيل الدخول";
            ignore = "إذا لم تكن تتوقع هذه الرسالة، تواصل مع مدير مؤسستك.";
            footer = "وابي إنسايت — اسأل بياناتك. وثق بالإجابة.";
        }
        else
        {
            subject = isNewAccount ? "Your Wabi Insight account" : "Your Wabi Insight password was reset";
            greeting = $"Hello {name},";
            intro = isNewAccount
                ? "An account has been created for you in Wabi Insight. Use the temporary password below for your first sign-in:"
                : "An administrator reset your Wabi Insight password. Use the temporary password below to sign in:";
            passwordLabel = "Temporary password";
            instruction = "You will be asked to change it immediately after signing in. The temporary password is for one-time use; do not share it.";
            signIn = "Sign in";
            ignore = "If you were not expecting this message, contact your organization administrator.";
            footer = "Wabi Insight — Ask Your Data. Trust the Answer.";
        }

        var dir = ar ? "rtl" : "ltr";
        var font = ar ? "'IBM Plex Sans Arabic', Arial, sans-serif" : "Inter, Arial, sans-serif";
        var html = $"""
            <!doctype html>
            <html lang="{(ar ? "ar" : "en")}" dir="{dir}">
            <body style="margin:0;background:#f5f8fc;font-family:{font};color:#0b1f3a;">
              <div style="max-width:560px;margin:24px auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:32px;direction:{dir};text-align:start;">
                <p style="font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#087df2;margin:0 0 12px;">Wabi Insight · AP-AIR</p>
                <p style="font-size:16px;margin:0 0 12px;">{greeting}</p>
                <p style="font-size:15px;line-height:1.6;margin:0 0 16px;">{intro}</p>
                <p style="font-size:13px;color:#334155;margin:0 0 4px;">{passwordLabel}</p>
                <p style="font-size:22px;font-weight:700;letter-spacing:.04em;direction:ltr;unicode-bidi:isolate;background:#f5f8fc;border:1px solid #e2e8f0;border-radius:10px;padding:12px 16px;margin:0 0 16px;">{pwd}</p>
                <p style="font-size:14px;line-height:1.6;color:#334155;margin:0 0 20px;">{instruction}</p>
                <p style="margin:0 0 24px;"><a href="{url}" style="display:inline-block;background:#087df2;color:#ffffff;text-decoration:none;font-weight:600;padding:12px 20px;border-radius:999px;">{signIn}</a></p>
                <p style="font-size:13px;color:#334155;margin:0 0 8px;">{ignore}</p>
                <p style="font-size:12px;color:#94a3b8;margin:0;">{footer}</p>
              </div>
            </body>
            </html>
            """;

        var text = $"{greeting}\n\n{intro}\n\n{passwordLabel}: {temporaryPassword}\n\n{instruction}\n\n{signIn}: {signInUrl}\n\n{ignore}\n\n{footer}\n";

        return new EmailMessage(toEmail, toName, subject, html, text, ar ? "ar" : "en") { ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo };
    }
}
