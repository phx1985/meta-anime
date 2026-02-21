using System.ComponentModel.DataAnnotations;

namespace MetaAnime.Application.Contracts;

public record UpsertPropertyBagRequest([Required] string Value);
