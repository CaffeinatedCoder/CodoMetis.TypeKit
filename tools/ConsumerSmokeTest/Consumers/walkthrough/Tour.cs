// The requests docs/walkthrough.md follows, sent to a running orders service. Each answer the page states in a
// comment is reported under the name of the variable that holds it, and the smoke test and WalkthroughTests
// compare the two (test/CodoMetis.TypeKit.Samples compiles this file too).
using System.Text;
using CodoMetis.TypeKit;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Walkthrough;

/// <summary>An HTTP answer as the page writes it: the status, a space, the body.</summary>
public sealed record Answer(int Status, string Body)
{
    public override string ToString() => $"{Status} {Body}";
}

public static class Tour
{
    public static async Task RunAsync(HttpClient http, Action<string, string> report)
    {
        async Task<Answer> Send(string method, string path, string? json = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            return new Answer((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        // sample: walkthrough/place-request
        Answer placed = await Send("POST", "/orders", """{"email":"  ann@example.com ","quantity":2}""");   // 201 {"id":"…","email":"ann@example.com","quantity":2,"status":"Placed"}
        OrderId id = JsonSerializer.Deserialize<OrderView>(placed.Body, JsonSerializerOptions.Web)!.Id;
        // end sample
        report(nameof(placed), placed.ToString());

        // sample: walkthrough/refused
        Answer noAt = await Send("POST", "/orders", """{"email":"ann","quantity":2}""");   // 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"…"}
        Answer noEmail = await Send("POST", "/orders", """{"quantity":2}""");              // 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"…"}
        Answer notAnId = await Send("GET", "/orders/42");                                  // 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"…"}
        Answer noneAtAll = await Send("GET", "/orders?minimum=0");                         // 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"…"}
        // end sample
        report(nameof(noAt), noAt.ToString());
        report(nameof(noEmail), noEmail.ToString());
        report(nameof(notAnId), notAnId.ToString());
        report(nameof(noneAtAll), noneAtAll.ToString());

        // sample: walkthrough/find-request
        Answer found = await Send("GET", $"/orders/{id}");               // 200 {"id":"…","email":"ann@example.com","quantity":2,"status":"Placed"}
        Answer missing = await Send("GET", $"/orders/{OrderId.New()}");   // 404 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"fault":"NotFound","traceId":"…"}
        // end sample
        report(nameof(found), found.ToString());
        report(nameof(missing), missing.ToString());

        // sample: walkthrough/list-request
        Answer atLeastTwo = await Send("GET", "/orders?minimum=2");   // 200 [{"id":"…","email":"ann@example.com","quantity":2,"status":"Placed"}]
        Answer atLeastFive = await Send("GET", "/orders?minimum=5");  // 200 []
        // end sample
        report(nameof(atLeastTwo), atLeastTwo.ToString());
        report(nameof(atLeastFive), atLeastFive.ToString());

        // sample: walkthrough/change-request
        Answer three = await Send("PATCH", $"/orders/{id}", """{"quantity":3}""");        // 200 {"id":"…","email":"ann@example.com","quantity":3,"status":"Placed"}
        Answer zero = await Send("PATCH", $"/orders/{id}", """{"quantity":0}""");         // 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"…"}
        Answer cleared = await Send("PATCH", $"/orders/{id}", """{"quantity":null}""");   // 422 {"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"Unprocessable Entity","status":422,"fault":"Cleared","traceId":"…"}
        Answer shipped = await Send("PATCH", $"/orders/{id}", """{"status":"Shipped"}""");   // 200 {"id":"…","email":"ann@example.com","quantity":3,"status":"Shipped"}
        Answer tooLate = await Send("PATCH", $"/orders/{id}", """{"quantity":4}""");      // 409 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Conflict","status":409,"fault":"AlreadyShipped","traceId":"…"}
        // end sample
        report(nameof(three), three.ToString());
        report(nameof(zero), zero.ToString());
        report(nameof(cleared), cleared.ToString());
        report(nameof(shipped), shipped.ToString());
        report(nameof(tooLate), tooLate.ToString());

        // sample: walkthrough/document
        JsonNode document = JsonNode.Parse(await http.GetStringAsync("/openapi/v1.json"))!;
        JsonNode schemas = document["components"]!["schemas"]!;
        JsonNode find = document["paths"]!["/orders/{id}"]!["get"]!;

        string idParameter = find["parameters"]![0]!["schema"]!.ToJsonString();                         // {"type":"string","format":"uuid"}
        string findAnswers = string.Join(",", find["responses"]!.AsObject().Select(answer => answer.Key));   // 200,404
        string emailSchema = schemas["Email"]!.ToJsonString();                                             // {"type":"string"}
        string statusSchema = schemas["OrderStatus"]!.ToJsonString();                                      // {"enum":["Placed","Shipped","Cancelled"],"type":"string"}
        string patchType = schemas["OrderPatch"]!["properties"]!["quantity"]!["type"]!.ToJsonString();    // ["null","integer","string"]
        // end sample
        report(nameof(idParameter), idParameter);
        report(nameof(findAnswers), findAnswers);
        report(nameof(emailSchema), emailSchema);
        report(nameof(statusSchema), statusSchema);
        report(nameof(patchType), patchType);
    }
}
