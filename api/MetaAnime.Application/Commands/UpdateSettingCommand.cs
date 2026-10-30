using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

/// <summary>
/// Settings-screen write: the shared property-bag upsert plus the job
/// re-scheduling that the settings route performs when an interval changes.
/// </summary>
public record UpdateSettingCommand(string Key, UpsertPropertyBagRequest Request) : IRequest<Result<PropertyBagResponse>>;
