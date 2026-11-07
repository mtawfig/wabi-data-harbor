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

    /// <summary>Generic bilingual notification (support desk events). Bodies are plain sentences; links go to the app.</summary>
    public static EmailMessage Notification(string toEmail, string toName, string language, string subjectEn, string subjectAr, string bodyEn, string bodyAr, string linkUrl, string replyTo = "")
    {
        var ar = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var subject = ar ? subjectAr : subjectEn;
        var body = ar ? bodyAr : bodyEn;
        var greeting = ar ? $"مرحبًا {WebUtility.HtmlEncode(toName)}،" : $"Hello {WebUtility.HtmlEncode(toName)},";
        var open = ar ? "فتح في وابي إنسايت" : "Open in Wabi Insight";
        var footer = ar ? "وابي إنسايت — اسأل بياناتك. وثق بالإجابة." : "Wabi Insight — Ask Your Data. Trust the Answer.";
        var dir = ar ? "rtl" : "ltr";
        var font = ar ? "'IBM Plex Sans Arabic', Arial, sans-serif" : "Inter, Arial, sans-serif";
        var url = WebUtility.HtmlEncode(linkUrl);
        var html = $"""
            <!doctype html>
            <html lang="{(ar ? "ar" : "en")}" dir="{dir}">
            <body style="margin:0;background:#f5f8fc;font-family:{font};color:#0b1f3a;">
              <div style="max-width:560px;margin:24px auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:32px;direction:{dir};text-align:start;">
                <p style="font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#087df2;margin:0 0 12px;">Wabi Insight · AP-AIR</p>
                <p style="font-size:16px;margin:0 0 12px;">{greeting}</p>
                <p style="font-size:15px;line-height:1.6;margin:0 0 20px;">{WebUtility.HtmlEncode(body)}</p>
                <p style="margin:0 0 24px;"><a href="{url}" style="display:inline-block;background:#087df2;color:#ffffff;text-decoration:none;font-weight:600;padding:12px 20px;border-radius:999px;">{open}</a></p>
                <p style="font-size:12px;color:#94a3b8;margin:0;">{footer}</p>
              </div>
            </body>
            </html>
            """;
        var text = $"{(ar ? $"مرحبًا {toName}،" : $"Hello {toName},")}\n\n{body}\n\n{open}: {linkUrl}\n\n{footer}\n";
        return new EmailMessage(toEmail, toName, subject, html, text, ar ? "ar" : "en") { ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo };
    }

    /// <summary>
    /// An analysis the owner sends by email (specification §24): the owner's own subject and body, the analysis title,
    /// an optional secure link for recipients who are users of the organization, and the report as an attachment.
    /// </summary>
    public static EmailMessage SharedAnalysis(string toEmail, string toName, string language, string subject, string body, string ownerName, string analysisTitle, string? linkUrl, string replyTo = "")
    {
        var ar = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var greeting = ar ? $"مرحبًا {WebUtility.HtmlEncode(toName)}،" : $"Hello {WebUtility.HtmlEncode(toName)},";
        var from = ar ? $"أرسل {WebUtility.HtmlEncode(ownerName)} هذا التحليل من وابي إنسايت: {WebUtility.HtmlEncode(analysisTitle)}" : $"{WebUtility.HtmlEncode(ownerName)} sent you this analysis from Wabi Insight: {WebUtility.HtmlEncode(analysisTitle)}";
        var open = ar ? "فتح التحليل" : "Open the analysis";
        var footer = ar ? "وابي إنسايت — اسأل بياناتك. وثق بالإجابة. أُرسلت هذه الرسالة بموافقة مالك التحليل؛ لا تُعد توجيهها دون إذن." : "Wabi Insight — Ask Your Data. Trust the Answer. Sent with the approval of the analysis owner; do not forward without permission.";
        var dir = ar ? "rtl" : "ltr";
        var font = ar ? "'IBM Plex Sans Arabic', Arial, sans-serif" : "Inter, Arial, sans-serif";
        var paragraphs = string.Join(string.Empty, body.Split('\n').Select(line => $"<p style=\"font-size:15px;line-height:1.6;margin:0 0 10px;\">{WebUtility.HtmlEncode(line)}</p>"));
        var link = linkUrl is null ? string.Empty : $"<p style=\"margin:16px 0 24px;\"><a href=\"{WebUtility.HtmlEncode(linkUrl)}\" style=\"display:inline-block;background:#087df2;color:#ffffff;text-decoration:none;font-weight:600;padding:12px 20px;border-radius:999px;\">{open}</a></p>";
        var html = $"""
            <!doctype html>
            <html lang="{(ar ? "ar" : "en")}" dir="{dir}">
            <body style="margin:0;background:#f5f8fc;font-family:{font};color:#0b1f3a;">
              <div style="max-width:640px;margin:24px auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:32px;direction:{dir};text-align:start;">
                <p style="font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#087df2;margin:0 0 12px;">Wabi Insight · AP-AIR</p>
                <p style="font-size:16px;margin:0 0 12px;">{greeting}</p>
                <p style="font-size:13px;color:#64748b;margin:0 0 16px;">{from}</p>
                {paragraphs}
                {link}
                <p style="font-size:12px;color:#94a3b8;margin:16px 0 0;">{footer}</p>
              </div>
            </body>
            </html>
            """;
        var textLink = linkUrl is null ? string.Empty : $"\n\n{open}: {linkUrl}";
        var text = $"{(ar ? $"مرحبًا {toName}،" : $"Hello {toName},")}\n\n{(ar ? $"أرسل {ownerName} هذا التحليل من وابي إنسايت: {analysisTitle}" : $"{ownerName} sent you this analysis from Wabi Insight: {analysisTitle}")}\n\n{body}{textLink}\n\n{footer}\n";
        return new EmailMessage(toEmail, toName, subject, html, text, ar ? "ar" : "en") { ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo };
    }

    /// <summary>
    /// The daily alert digest (owner decision D4): one email per recipient, grouped by severity. Each line carries the
    /// rule, the alert title and the repeat count; group keys and measures only when the recipient may read them.
    /// </summary>
    public static EmailMessage AlertDigest(string toEmail, string toName, string language, string organizationName, IReadOnlyList<Monitoring.DigestLine> lines, string linkUrl, string replyTo = "")
    {
        ArgumentNullException.ThrowIfNull(lines);
        var ar = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var subject = DigestSubject(ar, lines.Count);
        var greeting = ar ? $"مرحبًا {WebUtility.HtmlEncode(toName)}،" : $"Hello {WebUtility.HtmlEncode(toName)},";
        var intro = ar
            ? $"هذه تنبيهات {WebUtility.HtmlEncode(organizationName)} منذ الملخص السابق. لا يتضمن البريد أي بيانات خام؛ افتح مركز التنبيهات للمراجعة واتخاذ الإجراء."
            : $"These are the alerts for {WebUtility.HtmlEncode(organizationName)} since the previous digest. No raw data is included; open the alert centre to review and act.";
        var open = ar ? "فتح مركز التنبيهات" : "Open the alert centre";
        var footer = ar ? "وابي إنسايت — المساعد الاستباقي. التنبيه يُعلِم فقط؛ القرار للإنسان." : "Wabi Insight — proactive assistant. Alerts inform; people decide.";
        var dir = ar ? "rtl" : "ltr";
        var font = ar ? "'IBM Plex Sans Arabic', Arial, sans-serif" : "Inter, Arial, sans-serif";
        string SeverityLabel(Entities.Monitoring.AlertSeverity severity) => (severity, ar) switch
        {
            (Entities.Monitoring.AlertSeverity.High, true) => "عالية",
            (Entities.Monitoring.AlertSeverity.Medium, true) => "متوسطة",
            (_, true) => "معلومة",
            (Entities.Monitoring.AlertSeverity.High, false) => "High",
            (Entities.Monitoring.AlertSeverity.Medium, false) => "Medium",
            _ => "Info",
        };
        string Color(Entities.Monitoring.AlertSeverity severity) => severity switch
        {
            Entities.Monitoring.AlertSeverity.High => "#c0262d",
            Entities.Monitoring.AlertSeverity.Medium => "#b26a00",
            _ => "#087df2",
        };
        string Detail(Monitoring.DigestLine line)
        {
            if (line.Redacted)
            {
                return ar ? "التفاصيل مخفية: لا تملك صلاحية قراءة بيانات هذه القاعدة." : "Details hidden: you are not permitted to read this rule's data.";
            }

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(line.GroupKey) && line.GroupKey != "*")
            {
                parts.Add(line.GroupKey);
            }

            if (line.Measure is { } m)
            {
                parts.Add((ar ? "القيمة: " : "Value: ") + m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }

            return string.Join(" · ", parts);
        }

        var ordered = lines.OrderByDescending(l => l.Severity).ThenBy(l => l.RuleName, StringComparer.Ordinal).ToList();
        var rows = string.Join(string.Empty, ordered.Select(line =>
        {
            var times = line.Occurrences > 1 ? (ar ? $" (×{line.Occurrences})" : $" (×{line.Occurrences})") : string.Empty;
            return $"""
                <tr>
                  <td style="padding:10px 8px;border-bottom:1px solid #e2e8f0;vertical-align:top;white-space:nowrap;"><span style="display:inline-block;padding:2px 10px;border-radius:999px;background:{Color(line.Severity)};color:#fff;font-size:12px;">{SeverityLabel(line.Severity)}</span></td>
                  <td style="padding:10px 8px;border-bottom:1px solid #e2e8f0;font-size:14px;line-height:1.5;"><strong>{WebUtility.HtmlEncode(line.Title)}</strong>{times}<br><span style="color:#475569;font-size:13px;">{WebUtility.HtmlEncode(Detail(line))}</span></td>
                </tr>
                """;
        }));
        var url = WebUtility.HtmlEncode(linkUrl);
        var html = $"""
            <!doctype html>
            <html lang="{(ar ? "ar" : "en")}" dir="{dir}">
            <body style="margin:0;background:#f5f8fc;font-family:{font};color:#0b1f3a;">
              <div style="max-width:640px;margin:24px auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:32px;direction:{dir};text-align:start;">
                <p style="font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#087df2;margin:0 0 12px;">Wabi Insight · AP-AIR</p>
                <p style="font-size:16px;margin:0 0 12px;">{greeting}</p>
                <p style="font-size:15px;line-height:1.6;margin:0 0 16px;">{intro}</p>
                <table role="presentation" style="width:100%;border-collapse:collapse;margin:0 0 20px;">{rows}</table>
                <p style="margin:0 0 24px;"><a href="{url}" style="display:inline-block;background:#087df2;color:#ffffff;text-decoration:none;font-weight:600;padding:12px 20px;border-radius:999px;">{open}</a></p>
                <p style="font-size:12px;color:#94a3b8;margin:0;">{footer}</p>
              </div>
            </body>
            </html>
            """;
        var textLines = string.Join("\n", ordered.Select(l => $"- [{SeverityLabel(l.Severity)}] {l.Title}{(l.Occurrences > 1 ? $" (×{l.Occurrences})" : string.Empty)} {Detail(l)}".TrimEnd()));
        var text = $"{(ar ? $"مرحبًا {toName}،" : $"Hello {toName},")}\n\n{(ar ? $"تنبيهات {organizationName} منذ الملخص السابق:" : $"Alerts for {organizationName} since the previous digest:")}\n\n{textLines}\n\n{open}: {linkUrl}\n\n{footer}\n";
        return new EmailMessage(toEmail, toName, subject, html, text, ar ? "ar" : "en") { ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo };
    }

    public static string DigestSubject(bool arabic, int count) => arabic
        ? $"وابي إنسايت — ملخص التنبيهات اليومي ({count})"
        : $"Wabi Insight — daily alert digest ({count})";

    /// <summary>
    /// A Periodic Report delivery (specification §35.3). With data: the file is attached and a link opens "sent to me"
    /// in the app. Without permission: a short note, never anyone else's figures.
    /// </summary>
    public static EmailMessage PeriodicReport(string toEmail, string toName, string language, string reportName, string organizationName, bool hasData, string fileFormat, string linkUrl, string replyTo = "")
    {
        var ar = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var subject = ar ? $"وابي إنسايت — {reportName}" : $"Wabi Insight — {reportName}";
        var greeting = ar ? $"مرحبًا {toName}،" : $"Hello {toName},";
        var body = hasData
            ? (ar
                ? $"تقريرك الدوري \"{reportName}\" من {organizationName} مرفق ({fileFormat}). أُعدّت الأرقام وفق صلاحياتك أنت، وقد تختلف عن أرقام زملائك."
                : $"Your periodic report \"{reportName}\" from {organizationName} is attached ({fileFormat}). The figures were prepared under your own permissions and may differ from your colleagues'.")
            : (ar
                ? $"لا توجد بيانات متاحة لصلاحياتك في التقرير الدوري \"{reportName}\". لم يُرفق أي ملف. تواصل مع مدير المؤسسة إذا كنت تحتاج إلى هذه البيانات."
                : $"No data is available for your permissions in the periodic report \"{reportName}\", so no file is attached. Contact your organization administrator if you need this data.");
        var open = ar ? "فتح تقاريري" : "Open My Reports";
        var footer = ar ? "وابي إنسايت — التقارير الدورية. لا تُعد توجيه هذه الرسالة دون إذن." : "Wabi Insight — Periodic Reports. Do not forward without permission.";
        var dir = ar ? "rtl" : "ltr";
        var font = ar ? "'IBM Plex Sans Arabic', Arial, sans-serif" : "Inter, Arial, sans-serif";
        var html = $"""
            <!doctype html>
            <html lang="{(ar ? "ar" : "en")}" dir="{dir}">
            <body style="margin:0;background:#f5f8fc;font-family:{font};color:#0b1f3a;">
              <div style="max-width:560px;margin:24px auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:32px;direction:{dir};text-align:start;">
                <p style="font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#087df2;margin:0 0 12px;">Wabi Insight · AP-AIR</p>
                <p style="font-size:16px;margin:0 0 12px;">{WebUtility.HtmlEncode(greeting)}</p>
                <p style="font-size:15px;line-height:1.6;margin:0 0 20px;">{WebUtility.HtmlEncode(body)}</p>
                <p style="margin:0 0 24px;"><a href="{WebUtility.HtmlEncode(linkUrl)}" style="display:inline-block;background:#087df2;color:#ffffff;text-decoration:none;font-weight:600;padding:12px 20px;border-radius:999px;">{open}</a></p>
                <p style="font-size:12px;color:#94a3b8;margin:0;">{footer}</p>
              </div>
            </body>
            </html>
            """;
        var text = $"{greeting}\n\n{body}\n\n{open}: {linkUrl}\n\n{footer}\n";
        return new EmailMessage(toEmail, toName, subject, html, text, ar ? "ar" : "en") { ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo };
    }
}
