using BillingRecon.Application.Common;
using FluentValidation;

namespace BillingRecon.Application.Tests.Common;

/// <summary>
/// The conventions the API's registration by scanning relies on (ADR 0018): every command and query has exactly one
/// handler, a handler is a sealed class for one message, and it validates its message exactly when a validator for that
/// message exists. A handler that forgot its validator would accept anything; one that asked for a validator that does
/// not exist could not be created.
/// </summary>
public sealed class UseCaseConventionTests
{
    private static readonly Type[] HandlerContracts = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

    private static readonly Type[] Exported = typeof(AssemblyMarker).Assembly.GetExportedTypes();

    private static readonly Type[] Handlers = [.. Exported.Where(type => type.IsClass && !type.IsAbstract && HandledMessages(type).Length > 0)];

    private static readonly Type[] Messages =
    [
        .. Exported.Where(type => type.IsClass && (type.Name.EndsWith("Command", StringComparison.Ordinal) || type.Name.EndsWith("Query", StringComparison.Ordinal))),
    ];

    private static readonly Type[] Validators = [.. Exported.Where(type => type.IsClass && !type.IsAbstract && ValidatedType(type) is not null)];

    [Fact]
    public void There_are_handlers_and_messages_to_check()
    {
        Handlers.ShouldNotBeEmpty();
        Messages.ShouldNotBeEmpty();
        Validators.ShouldNotBeEmpty();
    }

    [Fact]
    public void Every_handler_is_a_sealed_class_that_handles_exactly_one_command_or_query()
    {
        foreach (var handler in Handlers)
        {
            handler.IsSealed.ShouldBeTrue($"{handler.Name} is not sealed");
            HandledMessages(handler).ShouldHaveSingleItem($"{handler.Name} handles more than one message");
            handler.Name.ShouldEndWith("Handler", customMessage: $"{handler.Name} does not end with Handler");
        }
    }

    [Fact]
    public void Every_command_and_query_has_exactly_one_handler()
    {
        foreach (var message in Messages)
        {
            Handlers.Count(handler => HandledMessages(handler).Contains(message)).ShouldBe(1, $"{message.Name} has no handler or several");
        }

        Handlers.SelectMany(HandledMessages).ShouldAllBe(message => Messages.Contains(message));
    }

    [Fact]
    public void A_handler_takes_the_validator_of_its_message_exactly_when_the_message_has_one()
    {
        foreach (var handler in Handlers)
        {
            var message = HandledMessages(handler).Single();
            var hasValidator = Validators.Any(validator => ValidatedType(validator) == message);
            var takesValidator = handler.GetConstructors().Single().GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(IValidator<>).MakeGenericType(message));

            takesValidator.ShouldBe(hasValidator, $"{handler.Name}: a validator for {message.Name} exists = {hasValidator}, the handler takes it = {takesValidator}");
        }
    }

    [Fact]
    public void Every_public_validator_validates_a_command_or_query()
    {
        foreach (var validator in Validators)
        {
            Messages.ShouldContain(ValidatedType(validator)!, $"{validator.Name} validates no command or query, so nothing would ever run it");
            validator.IsSealed.ShouldBeTrue($"{validator.Name} is not sealed");
        }
    }

    private static Type[] HandledMessages(Type type)
        => [.. type.GetInterfaces()
            .Where(contract => contract.IsGenericType && HandlerContracts.Contains(contract.GetGenericTypeDefinition()))
            .Select(contract => contract.GetGenericArguments()[0])];

    private static Type? ValidatedType(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
