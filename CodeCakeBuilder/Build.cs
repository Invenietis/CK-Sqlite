using Cake.Common.Build;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Solution;
using Cake.Common.Text;
using Cake.Common.Tools.DotNetCore;
using Cake.Common.Tools.DotNetCore.Build;
using Cake.Common.Tools.DotNetCore.Pack;
using Cake.Common.Tools.DotNetCore.Publish;
using Cake.Common.Tools.DotNetCore.Restore;
using Cake.Common.Tools.MSBuild;
using Cake.Common.Tools.NuGet;
using Cake.Common.Tools.NuGet.Pack;
using Cake.Common.Tools.NuGet.Push;
using Cake.Common.Tools.NuGet.Restore;
using Cake.Common.Tools.NUnit;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using CK.Text;
using Code.Cake;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CodeCake
{

    class ComponentProjects
    {
        public ComponentProjects( string configuration )
        {
            ComponentProjectPaths = new []
            {
                GetNet461BinFolder( "CK.Sqlite.Setup.Model", configuration ),
                GetNet461BinFolder( "CK.Sqlite.Setup.Runtime", configuration ),
                
                GetNetCoreBinFolder( "CK.Sqlite.Setup.Model", configuration ),
                GetNetCoreBinFolder( "CK.Sqlite.Setup.Runtime", configuration )
            };
        }

        public IReadOnlyList<NormalizedPath> ComponentProjectPaths { get; }

        static NormalizedPath GetNet461BinFolder( string name, string configuration )
        {
            return System.IO.Path.GetFullPath( name + "/bin/" + configuration + "/net461" );
        }

        static NormalizedPath GetNetCoreBinFolder( string name, string configuration )
        {
            string pathToFramework = System.IO.Path.GetFullPath( name + "/bin/" + configuration + "/netstandard2.0" );
            if( !Directory.Exists( pathToFramework ) )
            {
                pathToFramework = System.IO.Path.GetFullPath( name + "/bin/" + configuration + "/netcoreapp2.0/publish" );
            }
            return pathToFramework;
        }
    }


    [AddPath( "%UserProfile%/.nuget/packages/**/tools*" )]
    public partial class Build : CodeCakeHost
    {

        public Build()
        {
            Cake.Log.Verbosity = Verbosity.Diagnostic;

            const string solutionName = "CK-Sqlite";
            const string solutionFileName = solutionName + ".sln";

            var releasesDir = Cake.Directory( "CodeCakeBuilder/Releases" );
            var projects = Cake.ParseSolution( solutionFileName )
                                       .Projects
                                       .Where( p => !(p is SolutionFolder)
                                                    && p.Name != "CodeCakeBuilder" );

            // We do not generate NuGet packages for .Tests projects for this solution.
            var projectsToPublish = projects
                                        .Where( p => !p.Path.Segments.Contains( "Tests" ) );

            // Initialized by Build: the netstandard2.0/netcoreapp2.0 directory must exist
            // since we rely on them to find the target...
            ComponentProjects componentProjects = null;

            // The SimpleRepositoryInfo should be computed once and only once.
            SimpleRepositoryInfo gitInfo = Cake.GetSimpleRepositoryInfo();
            // This default global info will be replaced by Check-Repository task.
            // It is allocated here to ease debugging and/or manual work on complex build script.
            CheckRepositoryInfo globalInfo = new CheckRepositoryInfo { Version = gitInfo.SafeNuGetVersion };

            Task( "Check-Repository" )
                .Does( () =>
                {
                    globalInfo = StandardCheckRepository( projectsToPublish, gitInfo );
                    if( globalInfo.ShouldStop )
                    {
                        Cake.TerminateWithSuccess( "All packages from this commit are already available. Build skipped." );
                    }
                } );

            Task( "Clean" )
                .Does( () =>
                 {
                     Cake.CleanDirectories( projects.Select( p => p.Path.GetDirectory().Combine( "bin" ) ) );
                     Cake.CleanDirectories( releasesDir );
                     Cake.DeleteFiles( "Tests/**/TestResult*.xml" );
                 } );

            Task( "Build" )
                .IsDependentOn( "Check-Repository" )
                .IsDependentOn( "Clean" )
                .Does( () =>
                 {
                     StandardSolutionBuild( solutionFileName, gitInfo, globalInfo.BuildConfiguration );
                     // It has to be published here to inject the Version information.
                     componentProjects = new ComponentProjects( globalInfo.BuildConfiguration );
                     foreach( var pub in componentProjects.ComponentProjectPaths.Where( p => p.LastPart == "publish" ) )
                     {
                         Cake.DotNetCorePublish( pub.RemoveLastPart( 4 ),
                            new DotNetCorePublishSettings().AddVersionArguments( gitInfo, s =>
                            {
                                s.Framework = "netcoreapp2.0";
                                s.Configuration = globalInfo.BuildConfiguration;
                            } ) );
                     }
                 } );

            Task( "Unit-Testing" )
                .IsDependentOn( "Build" )
                .WithCriteria( () => Cake.InteractiveMode() == InteractiveMode.NoInteraction
                                     || Cake.ReadInteractiveOption( "RunUnitTests", "Run Unit Tests?", 'Y', 'N' ) == 'Y' )
                .Does( () =>
                 {
                     StandardUnitTests( globalInfo.BuildConfiguration, projects.Where( p => p.Name.EndsWith( ".Tests" ) ) );
                 } );

            Task( "Create-All-NuGet-Packages" )
                .WithCriteria( () => gitInfo.IsValid )
                .IsDependentOn( "Unit-Testing" )
                .Does( () =>
                 {
                     StandardCreateNuGetPackages( releasesDir, projectsToPublish, gitInfo, globalInfo.BuildConfiguration );
                 } );

            Task( "Push-Runtimes-and-Engines" )
                .IsDependentOn( "Unit-Testing" )
                .WithCriteria( () => gitInfo.IsValid )
                .Does( () =>
                {
                    var components = componentProjects.ComponentProjectPaths.Select( x => x.ToString() );

                    var storeConf = Cake.CKSetupCreateDefaultConfiguration();
                    if( globalInfo.IsLocalCIRelease )
                    {
                        storeConf.TargetStoreUrl = System.IO.Path.Combine( globalInfo.LocalFeedPath, "CKSetupStore" );
                    }
                    if( !storeConf.IsValid )
                    {
                        Cake.Information( "CKSetupStoreConfiguration is invalid. Skipped push to remote store." );
                        return;
                    }

                    Cake.Information( $"Using CKSetupStoreConfiguration: {storeConf}" );
                    if( !Cake.CKSetupAddComponentFoldersToStore( storeConf, components ) )
                    {
                        Cake.TerminateWithError( "Error while registering components in local temporary store." );
                    }
                    if( !Cake.CKSetupPushLocalToRemoteStore( storeConf ) )
                    {
                        Cake.TerminateWithError( "Error while pushing components to remote store." );
                    }
                } );

            Task( "Push-NuGet-Packages" )
                .IsDependentOn( "Create-All-NuGet-Packages" )
                .WithCriteria( () => gitInfo.IsValid )
                .Does( () =>
                 {
                     StandardPushNuGetPackages( globalInfo, releasesDir );
                 } );

            // The Default task for this script can be set here.
            Task( "Default" )
                .IsDependentOn( "Push-NuGet-Packages" )
                .IsDependentOn( "Push-Runtimes-and-Engines" );
        }

    }
}
