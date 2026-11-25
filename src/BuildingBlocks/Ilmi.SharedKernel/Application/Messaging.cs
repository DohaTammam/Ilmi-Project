using Ilmi.SharedKernel.Domain;

namespace Ilmi.SharedKernel.Application;

public interface ICommand<TResponse>;
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct);
}

/// <summary>Return type for commands that have nothing to return.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
