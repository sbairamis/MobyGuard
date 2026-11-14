using System.Net;
using System.Text;

namespace Tests;

public class FakeOutboundWebhookHandler : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly List<(string Url, string Body)> _requests = [];

    public IReadOnlyList<(string Url, string Body)> ReceivedRequests
    {
        get
        {
            lock (_lock)
            {
                return _requests.ToList();
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _requests.Clear();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        var body = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : string.Empty;

        lock (_lock)
        {
            _requests.Add((url, body));
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok", Encoding.UTF8, "text/plain")
        };
    }
}
