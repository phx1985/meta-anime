using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public record GetSettingsQuery : IRequest<Result<IEnumerable<SettingDefinition>>>;
