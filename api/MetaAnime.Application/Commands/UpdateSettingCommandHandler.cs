using Hangfire;
using Mediator;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Application.Contracts;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace MetaAnime.Application.Commands;

public sealed class UpdateSettingCommandHandler(
    IMediator mediator,
    IRecurringJobManager recurringJobManager,
    IServiceProvider serviceProvider)
    : IRequestHandler<UpdateSettingCommand, Result<PropertyBagResponse>>
{
    public async ValueTask<Result<PropertyBagResponse>> Handle(UpdateSettingCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new UpsertPropertyBagCommand(request.Key, request.Request), cancellationToken);
            if (result.IsFailure)
                return result;

            // Re-schedule the sync job whenever the sync interval setting changes.
            if (request.Key == SettingsKeys.AppSyncInterval)
            {
                await AnimeSyncJob.ScheduleAsync(recurringJobManager, serviceProvider);
            }
            else if (request.Key == SettingsKeys.DownloadStatsCacheInterval)
            {
                await DownloadStatsCacheJob.ScheduleAsync(recurringJobManager, serviceProvider);
            }

            return result;
        }
        catch (Exception ex)
        {
            return Result.Failure<PropertyBagResponse>(new(Constants.Errors.UpdateSetting, ex.Message));
        }
    }
}
