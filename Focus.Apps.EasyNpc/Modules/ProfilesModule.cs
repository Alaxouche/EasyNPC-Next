using Autofac;
using Focus.Analysis.Execution;
using Focus.Apps.EasyNpc.Profiles;
using System;

namespace Focus.Apps.EasyNpc.Modules
{
    public class ProfilesModule : Module
    {
        public string AutosavePath { get; set; } = string.Empty;

        protected override void Load(ContainerBuilder builder)
        {
            if (string.IsNullOrEmpty(AutosavePath))
                throw new InvalidOperationException($"{nameof(AutosavePath)} must be configured.");

            builder.RegisterType<ProfileEventLog>()
                .WithParameter(new NamedParameter("fileName", AutosavePath))
                .As<ISuspendableProfileEventLog>()
                .As<IProfileEventLog>()
                .As<IReadOnlyProfileEventLog>()
                .SingleInstance();
            builder.RegisterType<ProfilePolicy>()
                .As<IProfilePolicy>()
                .As<ILoadOrderAnalysisReceiver>()
                .SingleInstance();
            builder.RegisterType<ProfileFactory>().As<IProfileFactory>().SingleInstance();
            // Local mugshot packs are the fast, blocking source that fills the gallery. The lineup renders from these.
            builder.RegisterType<FileSystemMugshotRepository>().As<IMugshotRepository>()
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
                // Why does Autofac like to pass an empty array for IEnumerable<T>?
                .WithParameter(new NamedParameter("extensions", null))
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
                .SingleInstance();
            // NPC Face Finder is used separately (not in the blocking lineup): it fills installed cards that have no
            // local mugshot, asynchronously, so it never slows the gallery and never adds cards for mods you don't have.
            builder.RegisterType<Profiles.FaceFinder.FaceFinderClient>()
                .As<Profiles.FaceFinder.IFaceFinderClient>().SingleInstance();
            builder.RegisterType<Profiles.FaceFinder.FaceFinderMugshotRepository>().AsSelf().SingleInstance();
            builder.RegisterType<LineupBuilder>().As<ILineupBuilder>();
            builder.RegisterType<ProfileViewModel>();
        }
    }
}
