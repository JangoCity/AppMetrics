// Copyright (c) Allan hardy. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using App.Metrics;
using App.Metrics.Core;
using App.Metrics.DataProviders;
using App.Metrics.Infrastructure;
using App.Metrics.Internal;
using App.Metrics.Json;
using App.Metrics.Registries;
using App.Metrics.Reporters;
using App.Metrics.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable CheckNamespace
namespace Microsoft.Extensions.DependencyInjection.Extensions
// ReSharper restore CheckNamespace
{
    internal static class MetricsCoreServiceCollectionExtensions
    {
        private static readonly IReadOnlyDictionary<JsonSchemeVersion, Type> MetricsJsonBuilderVersionMapping =
            new ReadOnlyDictionary<JsonSchemeVersion, Type>(new Dictionary<JsonSchemeVersion, Type>
            {
                { JsonSchemeVersion.AlwaysLatest, typeof(MetricsJsonBuilderV1) },
                { JsonSchemeVersion.Version1, typeof(MetricsJsonBuilderV1) }
            });

        internal static IMetricsHost AddMetricsCore(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            return AddMetricsCore(services, setupAction: null, metricsContext: default(IMetricsContext));
        }

        internal static IMetricsHost AddMetricsCore(
            this IServiceCollection services,
            Action<AppMetricsOptions> setupAction,
            IMetricsContext metricsContext)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            var metricsEnvironment = new MetricsAppEnvironment(PlatformServices.Default.Application);

            services.TryAddSingleton<MetricsMarkerService, MetricsMarkerService>();

            services.ConfigureDefaultServices();
            services.AddDefaultHealthCheckServices(metricsEnvironment);
            services.AddDefaultReporterServices();
            services.AddDefaultJsonServices();
            services.AddMetricsCoreServices(metricsEnvironment, metricsContext);            

            if (setupAction != null)
            {
                services.Configure(setupAction);
            }

            return new MetricsHost(services, metricsEnvironment);
        }

        internal static void AddDefaultHealthCheckServices(this IServiceCollection services,
            IMetricsEnvironment environment)
        {
            services.TryAddSingleton<IHealthCheckRegistry, DefaultHealthCheckRegistry>();
            services.TryAddSingleton<IHealthCheckManager, DefaultHealthCheckManager>();

            services.AddHealthChecks(environment);
        }

        internal static void AddDefaultReporterServices(this IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IMetricReporterRegistry), provider =>
            {
                var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
                var context = provider.GetRequiredService<IMetricsContext>();                
                var options = provider.GetRequiredService<IOptions<AppMetricsOptions>>();

                var registry = new DefaultMetricReporterRegistry(context, loggerFactory);

                options.Value.Reporters(registry);

                return registry;
            });
        }

        internal static void AddDefaultJsonServices(this IServiceCollection services)
        {
            services.TryAddSingleton<MetricsJsonBuilderV1, MetricsJsonBuilderV1>();
            services.TryAddSingleton(typeof(IMetricsJsonBuilder), provider =>
            {
                var options = provider.GetRequiredService<IOptions<AppMetricsOptions>>();
                var jsonBuilderType = MetricsJsonBuilderVersionMapping[options.Value.JsonSchemeVersion];
                return provider.GetRequiredService(jsonBuilderType);
            });
        }

        internal static void AddMetricsCoreServices(this IServiceCollection services,
            IMetricsEnvironment environment, IMetricsContext metricsContext)
        {
            services.TryAddTransient<Func<string, IMetricGroupRegistry>>(provider =>
            {
                return group => new DefaultMetricGroupRegistry(group);
            });
            services.TryAddSingleton<IMetricsBuilder>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<AppMetricsOptions>>().Value;

                return new DefaultMetricsBuilder(options.SystemClock, options.DefaultSamplingType);
            });
            services.TryAddSingleton(typeof(IMetricsRegistry), provider =>
            {
                //TODO: AH - need to resolve env info. Create a test as well?
                var options = provider.GetRequiredService<IOptions<AppMetricsOptions>>();
                return new DefaultMetricsRegistry(options.Value.GlobalContextName, options.Value.DefaultSamplingType,
                    options.Value.SystemClock, EnvironmentInfo.Empty, provider.GetRequiredService<Func<string, IMetricGroupRegistry>>());
            });
            services.TryAddSingleton<IMetricsDataManager, DefaultMetricsDataManager>();
            services.TryAddSingleton(typeof(IClock), provider => provider.GetRequiredService<IOptions<AppMetricsOptions>>().Value.SystemClock);
           
            services.TryAddSingleton<EnvironmentInfoBuilder, EnvironmentInfoBuilder>();

            services.TryAddSingleton(typeof(IMetricsContext), provider =>
            {
                var options = provider.GetRequiredService<IOptions<AppMetricsOptions>>();
                var healthCheckRegistry = provider.GetRequiredService<IHealthCheckRegistry>();
                var healthCheckDataProvider = provider.GetRequiredService<IHealthCheckManager>();
                var metricsBuilder = provider.GetRequiredService<IMetricsBuilder>();

                if (!options.Value.DisableHealthChecks)
                {
                    options.Value.HealthCheckRegistry(healthCheckRegistry);
                }

                if (metricsContext == default(IMetricsContext))
                {
                    metricsContext = new DefaultMetricsContext(options.Value.GlobalContextName, options.Value.SystemClock,
                        provider.GetRequiredService<IMetricsRegistry>(), 
                        metricsBuilder, healthCheckDataProvider, provider.GetRequiredService<IMetricsDataManager>());
                }


                if (options.Value.DisableMetrics)
                {
                    metricsContext.Advanced.CompletelyDisableMetrics();
                }

                return metricsContext;
            });

            services.TryAddSingleton(provider => environment);
        }

        private static void ConfigureDefaultServices(this IServiceCollection services)
        {            
            services.AddOptions();
        }
    }
}