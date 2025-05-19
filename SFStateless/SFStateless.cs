using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACESim;
using ACESim.Util;
using ACESimBase.Util.Serialization;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

namespace SFStateless
{
    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class SFStateless : StatelessService
    {
        public SFStateless(StatelessServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Optional override to create listeners (e.g., TCP, HTTP) for this service replica to handle client or user requests.
        /// </summary>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[0];
        }

        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: Replace the following sample code with your own logic 
            //       or remove this RunAsync override if it's not needed in your service.

            long iterations = 0;

            string nodeID = Context?.NodeContext?.NodeName ?? "N/A";

            while (true)
            {
                try
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"{nodeID}: RunAsync loop");
                    //AzureBlob.SerializeObject("results", $"Starting-{nodeID}-{iterations}.txt", true, "success");

                    cancellationToken.ThrowIfCancellationRequested();

                    LitigGameEndogenousDisputesLauncher launcher = new LitigGameEndogenousDisputesLauncher();

                    await launcher.ParticipateInDistributedProcessing(
                        launcher.MasterReportNameForDistributedProcessing,
                        cancellationToken,
                        message => ServiceEventSource.Current.ServiceMessage(this.Context, $"{nodeID}: {message}", ++iterations)
                        );
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    try
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, "Working-{0}" + "-" + nodeID + "-" + ex.Message + ex.StackTrace, ++iterations);
                        AzureBlob.SerializeObject("results", $"EXCEPTION-{nodeID}-{iterations}.txt", true, ex.Message + ex.StackTrace);
                    }
                    catch
                    {

                    }
                }
            }
        }
    }
}
