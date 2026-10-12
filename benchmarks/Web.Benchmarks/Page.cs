using System.Text;

namespace Snail.Toolkit.Web.Benchmarks;

/// <summary>A page of the size and shape the audit read: about 150 KB, two hundred and more links, noise around the text.</summary>
/// <remarks>
/// Written here rather than recorded, so the benchmarks carry nobody's page: a head with scripts and styles, a
/// navigation bar, sections of prose in two scripts, tables, a block of verbatim text, an inline drawing and a footer.
/// The same seed gives the same page, so a timing is always of the same document.
/// </remarks>
public static class Page
{
    /// <summary>Gets the address the page was read from.</summary>
    public static Uri Address { get; } = new("https://www.example.org/companies/1234567890/card");

    /// <summary>Builds the page.</summary>
    /// <returns>The markup.</returns>
    public static string Build()
    {
        var words = new[]
        {
            "company", "register", "director", "address", "capital", "founded", "activity", "revenue",
            "компания", "реестр", "директор", "адрес", "капитал", "основана", "деятельность", "выручка",
        };
        var random = new Random(42);
        var html = new StringBuilder(160 * 1024);

        html.Append("<!DOCTYPE html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>Company card — Example register</title>");
        html.Append("<link rel=\"canonical\" href=\"https://www.example.org/companies/1234567890\">");

        for (var i = 0; i < 6; i++)
            html.Append("<script>window.dataLayer=window.dataLayer||[];function gtag(){dataLayer.push(arguments)}gtag('js',new Date());</script>");

        html.Append("<style>body{font-family:sans-serif}.nav a{margin:0 4px}.card td{padding:2px 6px}</style></head><body>");
        html.Append("<header class=\"nav\"><nav>");

        for (var i = 0; i < 60; i++)
        {
            html.Append("<a href=\"/section/").Append(i);
            html.Append("?utm_source=nav&amp;utm_medium=web\">Section ").Append(i).Append("</a>");
        }

        html.Append("</nav></header><main><h1>Company card</h1>");

        for (var section = 0; section < 40; section++)
        {
            html.Append("<section><h2>Part ").Append(section).Append("</h2>");

            for (var paragraph = 0; paragraph < 5; paragraph++)
            {
                html.Append("<p>");

                for (var word = 0; word < 60; word++)
                    html.Append(words[random.Next(words.Length)]).Append(' ');

                html.Append("<a href=\"https://partner").Append(random.Next(20));
                html.Append(".example.com/item/").Append(section * 10 + paragraph);
                html.Append("?id=").Append(random.Next(1000)).Append("&amp;gclid=abc\">source</a>");
                html.Append(" <a rel=\"nofollow\" href=\"/out/").Append(section).Append("\">elsewhere</a></p>");
            }

            html.Append("<table class=\"card\"><tr><th>Field</th><th>Value</th></tr>");

            for (var row = 0; row < 8; row++)
            {
                html.Append("<tr><td>").Append(words[row]);
                html.Append("</td><td>").Append(random.Next(100_000)).Append("</td></tr>");
            }

            html.Append("</table></section>");
        }

        html.Append("<pre>  line one\n    line two, indented\n  line three</pre>");
        html.Append("<svg viewBox=\"0 0 10 10\"><title>chart</title><rect width=\"10\" height=\"10\"/></svg>");
        html.Append("</main><footer><p>© Example register</p><a href=\"/about\">About</a><a href=\"mailto:info@example.org\">Write</a></footer>");
        html.Append("<noscript>Enable JavaScript</noscript><iframe src=\"/frame\"></iframe></body></html>");

        return html.ToString();
    }
}
