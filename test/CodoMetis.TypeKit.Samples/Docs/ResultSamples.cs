using CodoMetis.TypeKit;
using System.Diagnostics;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Docs.Results;

// sample: result/types
public readonly partial record struct CartId : IValue<Guid>;

public readonly partial record struct OrderId : IValue<Guid>;

public enum CheckoutFault { CartUnknown, CartEmpty, OutOfStock, PaymentRefused }

public sealed record Line(string Sku, int Quantity, decimal Price);

public sealed record Cart(CartId Id, IReadOnlyList<Line> Lines);

public sealed record Reservation(OrderId Id, decimal Total);

public sealed record Receipt(OrderId Id, decimal Total);
// end sample

public sealed record LineRequest(string Sku, int Quantity);

// sample: result/api-fault
public enum ApiFault { NotFound, Invalid, Unavailable }

static class Faults
{
    public static ApiFault ToApi(CheckoutFault fault) => fault switch
    {
        CheckoutFault.CartUnknown => ApiFault.NotFound,
        CheckoutFault.CartEmpty or CheckoutFault.OutOfStock => ApiFault.Invalid,
        CheckoutFault.PaymentRefused => ApiFault.Unavailable,
        _ => throw new UnreachableException(),
    };
}
// end sample

/// <summary>The checkout the page's examples call: two carts, a stock, a limit on one payment.</summary>
public sealed class Checkout
{
    private readonly Dictionary<CartId, Cart> carts;
    private readonly Dictionary<string, (int Stock, decimal Price)> stock = new() { ["A"] = (5, 10m), ["B"] = (5, 10m) };
    private readonly decimal limit = 100m;

    public Cart Cart { get; } = new(CartId.New(), [new Line("A", 2, 10m), new Line("B", 1, 10m)]);

    public Cart Empty { get; } = new(CartId.New(), []);

    public Checkout() => carts = new Dictionary<CartId, Cart> { [Cart.Id] = Cart, [Empty.Id] = Empty };

    public Result<Cart, CheckoutFault> FindCart(CartId id) => carts.GetValueOrNone(id).ToResult(CheckoutFault.CartUnknown);

    public Task<Result<Cart, CheckoutFault>> FindCartAsync(CartId id) => Task.FromResult(FindCart(id));

    public Task<Result<Cart, CheckoutFault>> LoadCartAsync(CartId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FindCart(id));
    }

    private bool InStock(Cart cart) => cart.Lines.All(line => stock.TryGetValue(line.Sku, out var item) && item.Stock >= line.Quantity);

    // sample: result/two-shapes
    public Result<Reservation, CheckoutFault> Reserve(Cart cart)   // a value, or a fault
    {
        if (cart.Lines.Count == 0) return Result.Error(CheckoutFault.CartEmpty);
        if (!InStock(cart)) return Result.Error(CheckoutFault.OutOfStock);

        return new Reservation(OrderId.New(), cart.Lines.Sum(line => line.Quantity * line.Price));   // a bare value is a success
    }

    public Result<CheckoutFault> Charge(decimal total) =>              // a success or a fault, and no value
        total <= limit ? Result.Success() : CheckoutFault.PaymentRefused;   // a bare error converts here, where nothing can be confused with it
    // end sample

    public Task<Result<Reservation, CheckoutFault>> ReserveAsync(Cart cart) => Task.FromResult(Reserve(cart));

    public Task<Result<CheckoutFault>> ChargeAsync(decimal total) => Task.FromResult(Charge(total));

    public Result<CheckoutFault> Invoice(CartId id) => FindCart(id).Bind(_ => Result<CheckoutFault>.Success());

    public Result<Line, CheckoutFault> ToLine(LineRequest request) =>
        stock.TryGetValue(request.Sku, out var item) && item.Stock >= request.Quantity
            ? new Line(request.Sku, request.Quantity, item.Price)
            : Result.Error(CheckoutFault.OutOfStock);
}

public static class Usage
{
    public static (string Outcome, ResultState State, Option<Reservation> Maybe) Reach(Checkout checkout, Cart cart, List<string> audit)
    {
        // sample: result/reach
        var reserved = checkout.Reserve(cart);

        string outcome = reserved.Match(reservation => $"reserved {reservation.Total}", fault => $"refused: {fault}");   // "reserved 30"
        ResultState state = reserved.State;                                                                                 // Success

        if (reserved.TryGetValue(out var reservation, out _) && checkout.Charge(reservation.Total)) audit.Add("charged");   // a Result<TError> converts to bool; a valued one never does

        Option<Reservation> maybe = reserved.ToOption();   // the fault dropped, where only presence matters
        // end sample

        return (outcome, state, maybe);
    }

    public static (string Paid, string Ledger, string Empty, Result<CheckoutFault> Settled, Result<Receipt, ApiFault> ForApi) Pipeline(Checkout checkout, CartId cartId, CartId emptyCartId, List<string> audit)
    {
        // sample: result/pipeline
        Result<Receipt, CheckoutFault> receipt = checkout.FindCart(cartId)              // Result<Cart, CheckoutFault>
            .Ensure(cart => cart.Lines.Count > 0, CheckoutFault.CartEmpty)             // a rule on the value: break it, and this is the error
            .Bind(checkout.Reserve)                                                    // a step that can fail itself
            .Map(reservation => new Receipt(reservation.Id, reservation.Total))        // a step that cannot
            .Tap(r => audit.Add($"receipt {r.Total}"))                                 // on a success only
            .TapError(fault => audit.Add($"refused: {fault}"));                        // on an error only

        Result<CheckoutFault> charged = receipt.Bind(r => checkout.Charge(r.Total));   // a command after a query

        string paid = receipt.Match(r => $"paid {r.Total}", fault => $"refused: {fault}");     // "paid 30"
        string ledger = charged.Match(() => "charged", fault => $"not charged: {fault}");      // "charged"
        string empty = checkout.FindCart(emptyCartId).Ensure(cart => cart.Lines.Count > 0, CheckoutFault.CartEmpty).Bind(checkout.Reserve).Match(r => "reserved", fault => $"refused: {fault}");   // "refused: CartEmpty"
        // end sample

        // sample: result/recover
        Result<CheckoutFault> settled = charged.Recover(fault => fault == CheckoutFault.PaymentRefused ? checkout.Invoice(cartId) : fault);   // Success
        // end sample

        // sample: result/map-error
        Result<Receipt, ApiFault> forApi = receipt.MapError(Faults.ToApi);   // Success, untouched; an error would be the ApiFault
        // end sample

        return (paid, ledger, empty, settled, forApi);
    }

    public static async Task<string> PipelineAsync(Checkout checkout, CartId cartId, List<string> audit)
    {
        // sample: result/async
        Result<CheckoutFault> chargedAsync = await checkout.FindCartAsync(cartId)        // Task<Result<Cart, CheckoutFault>>
            .EnsureAsync(cart => cart.Lines.Count > 0, CheckoutFault.CartEmpty)          // a rule on a pending result
            .BindAsync(cart => checkout.ReserveAsync(cart))                              // an asynchronous step
            .MapAsync(reservation => reservation.Total)                                  // a synchronous step on a pending result
            .BindAsync(total => checkout.ChargeAsync(total))                             // a command: Result<CheckoutFault> remains
            .TapErrorAsync(fault => audit.Add($"not charged: {fault}"));

        string asyncLedger = chargedAsync.Match(() => "charged", fault => $"not charged: {fault}");   // "charged"
        // end sample

        return asyncLedger;
    }

    public static async Task<string> OnTheCallersContext(Checkout checkout, CartId cartId)
    {
        // sample: result/context
        Result<Cart, CheckoutFault> found = await checkout.FindCartAsync(cartId);   // the await returns to the caller's context
        string lineCount = found.Map(cart => $"{cart.Lines.Count} lines").Match(text => text, fault => $"refused: {fault}");   // "2 lines", the selector on that context too
        // end sample

        return lineCount;
    }

    public static (Result<IReadOnlyList<Line>, CheckoutFault> Lines, Result<IReadOnlyList<Reservation>, CheckoutFault> All, Result<decimal, CheckoutFault> Amount, Result<Line, CheckoutFault> Bulk, string Summary, string Stopped) Collect(
        Checkout checkout, Cart cart, List<LineRequest> requested, List<LineRequest> refused, List<Result<Reservation, CheckoutFault>> reservations, Result<decimal, CheckoutFault> unitPrice, Result<int, CheckoutFault> quantity)
    {
        // sample: result/collect
        Result<IReadOnlyList<Line>, CheckoutFault> lines = requested.Traverse(checkout.ToLine);   // every request to a line, or the first fault
        Result<IReadOnlyList<Reservation>, CheckoutFault> all = reservations.Sequence();          // a list of results to a result of a list
        Result<decimal, CheckoutFault> amount = unitPrice.Zip(quantity, (price, count) => price * count);   // both successes, or the first error
        Result<Line, CheckoutFault> bulk = cart.Lines.FirstOrError(line => line.Quantity >= 10, CheckoutFault.OutOfStock);   // the first match, or the error

        string summary = requested.Traverse(checkout.ToLine).Match(list => $"{list.Count} lines", fault => $"refused: {fault}");   // "2 lines"
        string stopped = refused.Traverse(checkout.ToLine).Match(list => $"{list.Count} lines", fault => $"refused: {fault}");    // "refused: OutOfStock"
        // end sample

        return (lines, all, amount, bulk, summary, stopped);
    }

    public static async Task<(Result<IReadOnlyList<Reservation>, CheckoutFault> Reserved, Result<CheckoutFault> Charged, Result<IReadOnlyList<Cart>, CheckoutFault> Found)> CollectAsync(
        Checkout checkout, List<Cart> carts, List<decimal> totals, List<CartId> ids, CancellationToken cancellationToken)
    {
        // sample: result/collect-async
        Result<IReadOnlyList<Reservation>, CheckoutFault> reserved = await carts.TraverseAsync(checkout.ReserveAsync);     // one at a time, in order; nothing starts after a fault
        Result<CheckoutFault> charged = await totals.TraverseAsync(checkout.ChargeAsync);                                   // a command per element, likewise
        Result<IReadOnlyList<Cart>, CheckoutFault> found = await ids.TraverseAsync(checkout.LoadCartAsync, cancellationToken);   // the token checked before each, and handed to it
        // end sample

        return (reserved, charged, found);
    }

    public static async Task<(Result<Uri, Exception> Url, Result<Uri, UriFormatException> Narrowed, Result<Uri, ApiFault> AsFault, Result<Exception> Written, Result<string, Exception> Read)> Catching(string text, string path)
    {
        // sample: result/catch
        Result<Uri, Exception> url = Result.Catch(() => new Uri(text));                                        // Error: UriFormatException
        Result<Uri, UriFormatException> narrowed = Result.Catch<Uri, UriFormatException>(() => new Uri(text));   // that type only; any other propagates
        Result<Uri, ApiFault> asFault = Result.Catch(() => new Uri(text)).MapError(_ => ApiFault.Invalid);     // Error: Invalid
        Result<Exception> written = Result.Catch(() => File.WriteAllText(path, "receipt"));                     // Success, a command
        Result<string, Exception> read = await Result.CatchAsync(() => File.ReadAllTextAsync(path));           // Success: receipt
        // end sample

        return (url, narrowed, asFault, written, read);
    }

    public static async Task<(Result<Exception> Saved, int Failures)> CatchingAll(string draft, string copy)
    {
        // sample: result/catch-all
        Task both = Task.WhenAll(File.WriteAllTextAsync(draft, "a"), File.WriteAllTextAsync(copy, "b"));
        Result<Exception> saved = await Result.CatchAsync(() => both);   // Error: DirectoryNotFoundException, the first of the two
        int failures = both.Exception?.InnerExceptions.Count ?? 0;        // 2, every failure is on the task
        // end sample

        return (saved, failures);
    }

    public static Result<Receipt, CheckoutFault> Query(Checkout checkout, CartId cartId)
    {
        // sample: result/query
        Result<Receipt, CheckoutFault> receipt =
            from cart in checkout.FindCart(cartId)
            from reservation in checkout.Reserve(cart)       // each from sees the earlier ranges
            select new Receipt(reservation.Id, reservation.Total);
        // end sample

        return receipt;
    }
}

public sealed class ResultTests
{
    private const string Readme = "docs/result.md";

    private static string Quoted(string text) => $"\"{text}\"";

    private static string Outcome<T, TError>(Result<T, TError> result) where T : notnull where TError : notnull =>
        result.Match(value => $"Success: {value}", error => $"Error: {(error is Exception e ? e.GetType().Name : error)}");

    private static string Outcome<TError>(Result<TError> result) where TError : notnull =>
        result.Match(() => "Success", error => $"Error: {error}");

    [Fact]
    public void Reaching_the_outcome_answers_what_the_comments_say()
    {
        var checkout = new Checkout();
        var cart = checkout.Cart;
        var audit = new List<string>();
        var reserved = checkout.Reserve(cart);

        reserved.Match(reservation => $"reserved {reservation.Total}", fault => $"refused: {fault}").ShouldBeShownIn(Readme, Quoted);
        reserved.State.ShouldBeShownIn(Readme, state => state.ToString());

        var (outcome, state, maybe) = Usage.Reach(checkout, cart, audit);
        (outcome, state, maybe.IsSome()).ShouldBe(("reserved 30", ResultState.Success, true));
        audit.ShouldBe(["charged"]);

        checkout.Reserve(checkout.Empty).State.ShouldBe(ResultState.Error);
        checkout.Charge(1_000m).TryGetError(out var refused).ShouldBeTrue();
        refused.ShouldBe(CheckoutFault.PaymentRefused);
    }

    [Fact]
    public void The_pipeline_answers_what_the_comments_say()
    {
        var checkout = new Checkout();
        var audit = new List<string>();

        var (paid, ledger, empty, settled, forApi) = Usage.Pipeline(checkout, checkout.Cart.Id, checkout.Empty.Id, audit);

        paid.ShouldBeShownIn(Readme, Quoted, "receipt.Match(r => $\"paid {r.Total}\", fault => $\"refused: {fault}\")");
        ledger.ShouldBeShownIn(Readme, Quoted, "charged.Match(() => \"charged\", fault => $\"not charged: {fault}\")");
        empty.ShouldBeShownIn(Readme, Quoted, "checkout.FindCart(emptyCartId).Ensure(cart => cart.Lines.Count > 0, CheckoutFault.CartEmpty).Bind(checkout.Reserve).Match(r => \"reserved\", fault => $\"refused: {fault}\")");
        settled.ShouldBeShownIn(Readme, Outcome, "charged.Recover(fault => fault == CheckoutFault.PaymentRefused ? checkout.Invoice(cartId) : fault)");
        forApi.ShouldBeShownIn(Readme, result => result.Match(_ => "Success", fault => $"Error: {fault}"), "receipt.MapError(Faults.ToApi)");
        audit.ShouldBe(["receipt 30"], "the empty cart's chain has no TapError");

        checkout.Charge(1_000m).Recover(fault => fault == CheckoutFault.PaymentRefused ? checkout.Invoice(checkout.Cart.Id) : fault).State.ShouldBe(ResultState.Success, "Recover ran on the error");
        checkout.Reserve(checkout.Empty).MapError(Faults.ToApi).TryGetValue(out _, out var api).ShouldBeFalse();
        api.ShouldBe(ApiFault.Invalid);
    }

    [Fact]
    public async Task The_async_chain_answers_what_the_comment_says()
    {
        var checkout = new Checkout();
        var audit = new List<string>();

        var ledger = await Usage.PipelineAsync(checkout, checkout.Cart.Id, audit);

        ledger.ShouldBeShownIn(Readme, Quoted, "chargedAsync.Match(() => \"charged\", fault => $\"not charged: {fault}\")");
        audit.ShouldBeEmpty();

        (await Usage.PipelineAsync(checkout, checkout.Empty.Id, audit)).ShouldBe("not charged: CartEmpty");
        audit.ShouldBe(["not charged: CartEmpty"]);
    }

    [Fact]
    public async Task Continuing_after_the_await_answers_what_the_comment_says()
    {
        var checkout = new Checkout();

        var lineCount = await Usage.OnTheCallersContext(checkout, checkout.Cart.Id);

        lineCount.ShouldBeShownIn(Readme, Quoted, "found.Map(cart => $\"{cart.Lines.Count} lines\").Match(text => text, fault => $\"refused: {fault}\")");
        (await Usage.OnTheCallersContext(checkout, CartId.New())).ShouldBe("refused: CartUnknown");
    }

    [Fact]
    public void Collecting_answers_what_the_comments_say()
    {
        var checkout = new Checkout();
        List<LineRequest> requested = [new("A", 1), new("B", 2)];
        List<LineRequest> refused = [new("A", 1), new("Z", 1), new("B", 2)];
        List<Result<Reservation, CheckoutFault>> reservations = [checkout.Reserve(checkout.Cart), checkout.Reserve(checkout.Empty)];
        Result<decimal, CheckoutFault> unitPrice = 10m;
        Result<int, CheckoutFault> quantity = 3;

        var (lines, all, amount, bulk, summary, stopped) = Usage.Collect(checkout, checkout.Cart, requested, refused, reservations, unitPrice, quantity);

        summary.ShouldBeShownIn(Readme, Quoted, "requested.Traverse(checkout.ToLine).Match(list => $\"{list.Count} lines\", fault => $\"refused: {fault}\")");
        stopped.ShouldBeShownIn(Readme, Quoted, "refused.Traverse(checkout.ToLine).Match(list => $\"{list.Count} lines\", fault => $\"refused: {fault}\")");
        lines.TryGetValue(out var list, out _).ShouldBeTrue();
        list.Select(line => line.Sku).ShouldBe(["A", "B"]);
        all.TryGetValue(out _, out var sequenced).ShouldBeFalse();
        sequenced.ShouldBe(CheckoutFault.CartEmpty);
        amount.TryGetValue(out var product, out _).ShouldBeTrue();
        product.ShouldBe(30m);
        bulk.TryGetValue(out _, out var noBulk).ShouldBeFalse();
        noBulk.ShouldBe(CheckoutFault.OutOfStock);
    }

    [Fact]
    public async Task Collecting_asynchronously_runs_one_operation_at_a_time_and_stops_at_a_fault()
    {
        var checkout = new Checkout();

        var (reserved, charged, found) = await Usage.CollectAsync(checkout, [checkout.Cart, checkout.Empty], [10m, 20m], [checkout.Cart.Id], TestContext.Current.CancellationToken);

        reserved.TryGetValue(out _, out var fault).ShouldBeFalse();
        fault.ShouldBe(CheckoutFault.CartEmpty);
        ((bool)charged).ShouldBeTrue();
        found.TryGetValue(out var carts, out _).ShouldBeTrue();
        carts.ShouldBe([checkout.Cart]);
    }

    [Fact]
    public async Task Catch_answers_what_the_comments_say()
    {
        var text = "not a url";
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var (url, narrowed, asFault, written, read) = await Usage.Catching(text, path);

            url.ShouldBeShownIn(Readme, Outcome, "Result.Catch(() => new Uri(text))");
            asFault.ShouldBeShownIn(Readme, Outcome, "Result.Catch(() => new Uri(text)).MapError(_ => ApiFault.Invalid)");
            written.ShouldBeShownIn(Readme, Outcome, "Result.Catch(() => File.WriteAllText(path, \"receipt\"))");
            read.ShouldBeShownIn(Readme, Outcome, "await Result.CatchAsync(() => File.ReadAllTextAsync(path))");
            narrowed.TryGetValue(out _, out var error).ShouldBeFalse();
            error.ShouldBeOfType<UriFormatException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CatchAsync_over_WhenAll_answers_what_the_comments_say()
    {
        var missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var draft = Path.Combine(missing, "draft.txt");
        var copy = Path.Combine(missing, "copy.txt");

        var (saved, failures) = await Usage.CatchingAll(draft, copy);

        saved.ShouldBeShownIn(Readme, result => result.Match(() => "Success", error => $"Error: {error.GetType().Name}"), "await Result.CatchAsync(() => both)");
        failures.ShouldBeShownIn(Readme, count => count.ToString(System.Globalization.CultureInfo.InvariantCulture), "both.Exception?.InnerExceptions.Count ?? 0");
        saved.TryGetError(out var first).ShouldBeTrue();
        first.Message.ShouldContain("draft.txt", Case.Sensitive, "the first operation's failure, not the second's");
    }

    [Fact]
    public void Query_syntax_binds_each_step()
    {
        var checkout = new Checkout();

        Usage.Query(checkout, checkout.Cart.Id).TryGetValue(out var receipt, out _).ShouldBeTrue();
        receipt.Total.ShouldBe(30m);
        Usage.Query(checkout, CartId.New()).TryGetValue(out _, out var fault).ShouldBeFalse();
        fault.ShouldBe(CheckoutFault.CartUnknown);
    }

    /// <summary>
    /// The page's surface tables name every public member of both result shapes and of the static class
    /// <c>Result</c>, extensions included, read from the types, so a member added without a row fails here.
    /// </summary>
    [Fact]
    public void The_surface_tables_name_every_public_member()
    {
        var page = SampleOutputs.Readme(Readme);
        var surface = page[page.IndexOf("## The surface", StringComparison.Ordinal)..];

        var members = Options.OptionTests.PublicMembers(typeof(Result<,>), typeof(Result<>), typeof(Result));

        members.Length.ShouldBeGreaterThanOrEqualTo(30, string.Join(", ", members));
        members.Where(member => !System.Text.RegularExpressions.Regex.IsMatch(surface, $@"`{System.Text.RegularExpressions.Regex.Escape(member)}[`(<,]"))
               .ShouldBeEmpty("Every public member has a row in a surface table.");
    }
}
