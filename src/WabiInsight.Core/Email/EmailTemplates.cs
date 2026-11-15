using System.Net;

namespace WabiInsight.Core.Email;

/// <summary>Bilingual transactional templates. Arabic messages are rendered RTL.</summary>
public static class EmailTemplates
{
    /// <summary>
    /// The self-service reset link (P13.32). The link is the only secret in the message, works once and stops working
    /// after <paramref name="validMinutes"/> minutes; no password is ever sent.
    /// </summary>
    public static EmailMessage PasswordResetLink(string toEmail, string toName, string resetUrl, int validMinutes, string language, string replyTo = "")
    {
        var ar = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var minutes = validMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var subject = ar ? "إعادة تعيين كلمة المرور في وابي إنسايت" : "Reset your Wabi Insight password";
        var greeting = ar ? $"مرحبًا {toName}،" : $"Hello {toName},";
        var intro = ar
            ? "وصلنا طلب لإعادة تعيين كلمة المرور لحسابك في وابي إنسايت. اضغط الزر التالي لاختيار كلمة مرور جديدة:"
            : "We received a request to reset the password of your Wabi Insight account. Use the button below to choose a new password:";
        var button = ar ? "اختيار كلمة مرور جديدة" : "Choose a new password";
        var validity = ar
            ? $"الرابط صالح لمدة {minutes} دقيقة ويُستخدم مرة واحدة فقط. لا تشاركه مع أحد."
            : $"The link works for {minutes} minutes and can be used once. Do not share it.";
        var fallback = ar ? "إذا لم يعمل الزر، انسخ هذا الرابط إلى المتصفح:" : "If the button does not work, copy this link into your browser:";
        var ignore = ar
            ? "إذا لم تطلب ذلك فتجاهل هذه الرسالة: كلمة مرورك الحالية تبقى كما هي."
            : "If you did not ask for this, ignore this message: your current password stays as it is.";
        var text = $"{greeting}\n\n{intro}\n{resetUrl}\n\n{validity}\n\n{ignore}\n";
        return Compose(toEmail, toName, subject, language, replyTo, text, greeting, [intro], (button, resetUrl), [validity, fallback], resetUrl, ignore);
    }

    /// <summary>Tells the account's owner that the password was just changed through a reset link (P13.32).</summary>
    public static EmailMessage PasswordChangedNotice(string toEmail, string toName, string signInUrl, string language, bool platformAdministrator, string replyTo = "")
    {
        var ar = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var subject = ar ? "تم تغيير كلمة المرور في وابي إنسايت" : "Your Wabi Insight password was changed";
        var greeting = ar ? $"مرحبًا {toName}،" : $"Hello {toName},";
        var intro = ar
            ? "تم تغيير كلمة المرور لحسابك في وابي إنسايت باستخدام رابط إعادة التعيين. أُنهيت جميع الجلسات المفتوحة، ويمكنك تسجيل الدخول بكلمة المرور الجديدة."
            : "The password of your Wabi Insight account was just changed with a reset link. Every open session was ended, and you can sign in with the new password.";
        var button = ar ? "تسجيل الدخول" : "Sign in";
        var ignore = (ar, platformAdministrator) switch
        {
            (true, true) => "إذا لم تكن أنت من غيّرها، اطلب من مدير منصة آخر إعادة تعيين كلمة مرورك فورًا.",
            (true, false) => "إذا لم تكن أنت من غيّرها، تواصل مع مدير مؤسستك فورًا.",
            (false, true) => "If it was not you, ask another Platform Administrator to reset your password at once.",
            _ => "If it was not you, contact your organization administrator at once.",
        };
        var text = $"{greeting}\n\n{intro}\n{signInUrl}\n\n{ignore}\n";
        return Compose(toEmail, toName, subject, language, replyTo, text, greeting, [intro], (button, signInUrl), [], null, ignore);
    }

    /// <summary>The shared layout of the account messages: greeting, paragraphs, one button, notes, an optional plain link.</summary>
    private static EmailMessage Compose(string toEmail, string toName, string subject, string language, string replyTo, string text, string greeting, string[] paragraphs, (string Label, string Url) button, string[] notes, string? plainLink, string closing)
    {
        var ar = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var dir = ar ? "rtl" : "ltr";
        var font = ar ? "'IBM Plex Sans Arabic', Arial, sans-serif" : "Inter, Arial, sans-serif";
        var footer = ar ? "وابي إنسايت — اسأل بياناتك. وثق بالإجابة." : "Wabi Insight — Ask Your Data. Trust the Answer.";
        var body = new System.Text.StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            body.Append(System.Globalization.CultureInfo.InvariantCulture, $"<p style=\"font-size:15px;line-height:1.6;margin:0 0 16px;\">{WebUtility.HtmlEncode(paragraph)}</p>");
        }

        body.Append(System.Globalization.CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 20px;\"><a href=\"{WebUtility.HtmlEncode(button.Url)}\" style=\"display:inline-block;background:#087df2;color:#ffffff;text-decoration:none;font-weight:600;padding:12px 20px;border-radius:999px;\">{WebUtility.HtmlEncode(button.Label)}</a></p>");
        foreach (var note in notes)
        {
            body.Append(System.Globalization.CultureInfo.InvariantCulture, $"<p style=\"font-size:14px;line-height:1.6;color:#334155;margin:0 0 8px;\">{WebUtility.HtmlEncode(note)}</p>");
        }

        if (plainLink is not null)
        {
            body.Append(System.Globalization.CultureInfo.InvariantCulture, $"<p style=\"font-size:13px;direction:ltr;unicode-bidi:isolate;word-break:break-all;color:#087df2;margin:0 0 20px;\">{WebUtility.HtmlEncode(plainLink)}</p>");
        }

        var html = $"""
            <!doctype html>
            <html lang="{(ar ? "ar" : "en")}" dir="{dir}">
            <body style="margin:0;background:#f5f8fc;font-family:{font};color:#0b1f3a;">
              <div style="max-width:560px;margin:24px auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:32px;direction:{dir};text-align:start;">
                <p style="font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#087df2;margin:0 0 12px;">Wabi Insight · AP-AIR</p>
                <p style="font-size:16px;margin:0 0 12px;">{WebUtility.HtmlEncode(greeting)}</p>
                {body}
                <p style="font-size:13px;color:#334155;margin:0 0 8px;">{WebUtility.HtmlEncode(closing)}</p>
                <p style="font-size:12px;color:#94a3b8;margin:0;">{WebUtility.HtmlEncode(footer)}</p>
              </div>
            </body>
            </html>
            """;
        return new EmailMessage(toEmail, toName, subject, html, text, ar ? "ar" : "en") { ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo };
    }

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

    /// <summary>
    /// Tells the team's mailbox about a new demo request (P13.52), in English, with what the visitor sent. A reply goes
    /// to the visitor. The values are the visitor's own words: they are encoded like any other text.
    /// </summary>
    public static EmailMessage DemoRequestReceived(string toEmail, string fullName, string organizationName, IReadOnlyList<(string Label, string Value)> details, string openUrl, string visitorEmail)
    {
        var subject = $"New demo request: {organizationName}";
        var greeting = "A demo was requested from the Wabi Insight landing page.";
        var lines = details.Where(d => !string.IsNullOrWhiteSpace(d.Value)).Select(d => $"{d.Label}: {d.Value}").ToArray();
        var closing = "Reply to this message to answer the person who asked.";
        var text = $"{greeting}\n\n{string.Join("\n", lines)}\n\n{openUrl}\n\n{closing}\n";
        return Compose(toEmail, "Wabi Insight", subject, "en", visitorEmail, text, greeting, lines, ("Open the request", openUrl), [], null, closing);
    }

    /// <summary>Confirms to the visitor that the demo request arrived, in the language of the page they used (P13.52).</summary>
    public static EmailMessage DemoRequestConfirmation(string toEmail, string toName, string language, string homeUrl, string replyTo = "")
    {
        var ar = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var subject = ar ? "وصلنا طلبك لعرض وابي إنسايت" : "We received your Wabi Insight demo request";
        var greeting = ar ? $"مرحبًا {toName}،" : $"Hello {toName},";
        var intro = ar
            ? "شكرًا لاهتمامك بوابي إنسايت. وصلنا طلبك لعرض توضيحي، وسيتواصل معك أحد أفراد فريقنا خلال يومي عمل لتحديد الموعد المناسب."
            : "Thank you for your interest in Wabi Insight. We received your request for a demo, and a member of our team will contact you within two working days to agree a time.";
        var button = ar ? "زيارة وابي إنسايت" : "Visit Wabi Insight";
        var ignore = ar
            ? "إذا لم تطلب ذلك فتجاهل هذه الرسالة، أو ردّ عليها لنحذف بياناتك."
            : "If you did not ask for this, ignore this message, or reply to it and we will delete your details.";
        var text = $"{greeting}\n\n{intro}\n\n{homeUrl}\n\n{ignore}\n";
        return Compose(toEmail, toName, subject, language, replyTo, text, greeting, [intro], (button, homeUrl), [], null, ignore);
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
                return ar ? "التفاصيل مخفية: صلاحياتك لا تشمل كل بيانات هذه القاعدة." : "Details hidden: your permissions do not cover all of this rule's data.";
            }

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(line.GroupKey) && line.GroupKey != "*")
            {
                parts.Add(line.GroupKey);
            }

            if (line.Measure is { } m)
            {
                parts.Add((ar ? "القيمة: " : "Value: ") + Common.NumberText.Format(m, ar));
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
