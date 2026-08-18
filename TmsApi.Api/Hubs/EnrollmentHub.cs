using Microsoft.AspNetCore.SignalR;
using TmsApi.Application.Hubs;

namespace TmsApi.Api.Hubs;

public class EnrollmentHub : Hub<ITmsHubClient>
{
}