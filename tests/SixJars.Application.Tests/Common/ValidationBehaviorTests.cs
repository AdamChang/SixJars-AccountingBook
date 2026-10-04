using FluentAssertions;
using FluentValidation;
using MediatR;
using SixJars.Application.Common;
using Xunit;

namespace SixJars.Application.Tests.Common;

public class ValidationBehaviorTests
{
    private sealed record Probe(string Name) : IRequest<string>;

    private sealed class ProbeValidator : AbstractValidator<Probe>
    {
        public ProbeValidator() => RuleFor(p => p.Name).NotEmpty();
    }

    [Fact]
    public async Task Passes_valid_requests_to_the_handler()
    {
        var behavior = new ValidationBehavior<Probe, string>([new ProbeValidator()]);

        var result = await behavior.Handle(new Probe("ok"), _ => Task.FromResult("handled"), TestContext.Current.CancellationToken);

        result.Should().Be("handled");
    }

    [Fact]
    public async Task Throws_without_calling_the_handler_when_invalid()
    {
        var behavior = new ValidationBehavior<Probe, string>([new ProbeValidator()]);
        var called = false;

        var act = () => behavior.Handle(new Probe(""), _ => { called = true; return Task.FromResult(""); }, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
        called.Should().BeFalse();
    }
}
