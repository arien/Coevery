﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Autofac;
using NuGet;
using NUnit.Framework;
using Coevery.Caching;
using Coevery.Core.Settings.Descriptor;
using Coevery.Core.Settings.Descriptor.Records;
using Coevery.Core.Settings.State;
using Coevery.Data.Migration;
using Coevery.Environment.Configuration;
using Coevery.Environment.Descriptor;
using Coevery.Environment.Descriptor.Models;
using Coevery.Environment.Extensions;
using Coevery.Environment.Extensions.Folders;
using Coevery.Environment.Extensions.Models;
using Coevery.Environment.Features;
using Coevery.Environment.State;
using Coevery.Events;
using Coevery.Packaging.GalleryServer;
using Coevery.Packaging.Models;
using Coevery.Packaging.Services;
using Coevery.Recipes.Models;
using Coevery.Recipes.RecipeHandlers;
using Coevery.Recipes.Services;
using Coevery.Tests.Environment.Extensions;
using Coevery.Tests.Environment.Features;
using Coevery.Tests.Stubs;
using IPackageManager = Coevery.Packaging.Services.IPackageManager;

namespace Coevery.Tests.Modules.Recipes.RecipeHandlers {
    [TestFixture]
    public class ModuleRecipeHandlerTest : DatabaseEnabledTestsBase {
        private ExtensionManagerTests.StubFolders _folders;
        private StubPackagingSourceManager _packagesInRepository;
        private StubPackageManager _packageManager;

        protected override IEnumerable<Type> DatabaseTypes {
            get {
                return new[] {
                    typeof (ShellDescriptorRecord),
                    typeof (ShellFeatureRecord),
                    typeof (ShellParameterRecord),
                };
            }
        }

        public override void Register(ContainerBuilder builder) {
            builder.RegisterInstance(new ShellSettings { Name = "Default" });

            _folders = new ExtensionManagerTests.StubFolders();
            _packagesInRepository = new StubPackagingSourceManager();
            _packageManager = new StubPackageManager();
            builder.RegisterInstance(_folders).As<IExtensionFolders>();
            builder.RegisterType<ExtensionManager>().As<IExtensionManager>();
            builder.RegisterType<FeatureManager>().As<IFeatureManager>();
            builder.RegisterType<StubCacheManager>().As<ICacheManager>();
            builder.RegisterType<StubParallelCacheContext>().As<IParallelCacheContext>();
            builder.RegisterType<StubAsyncTokenProvider>().As<IAsyncTokenProvider>();
            builder.RegisterType<ShellDescriptorManager>().As<IShellDescriptorManager>().SingleInstance();
            builder.RegisterType<StubDataMigrationManager>().As<IDataMigrationManager>();
            builder.RegisterInstance(_packagesInRepository).As<IPackagingSourceManager>();
            builder.RegisterInstance(_packageManager).As<IPackageManager>();
            builder.RegisterType<ShellStateManager>().As<IShellStateManager>().SingleInstance();
            builder.RegisterType<StubEventBus>().As<IEventBus>().SingleInstance();
            builder.RegisterType<ModuleRecipeHandler>();
            builder.RegisterSource(new EventsRegistrationSource());
        }

        [Test]
        public void ExecuteRecipeStepTest() {
            _folders.Manifests.Add("SuperWiki", @"
Name: SuperWiki
Version: 1.0.3
CoeveryVersion: 1
Features:
    SuperWiki: 
        Description: My super wiki module for Coevery.
");
            _packagesInRepository.AddPublishedPackage(new PublishedPackage {
                Id = "Coevery.Module.SuperWiki",
                PackageType = DefaultExtensionTypes.Module,
                Title = "SuperWiki",
                Version = "1.0.3",
                IsLatestVersion = true,
            });

            IShellDescriptorManager shellDescriptorManager = _container.Resolve<IShellDescriptorManager>();
            // No features enabled
            shellDescriptorManager.UpdateShellDescriptor(0,
                                                         Enumerable.Empty<ShellFeature>(),
                                                         Enumerable.Empty<ShellParameter>());

            ModuleRecipeHandler moduleRecipeHandler = _container.Resolve<ModuleRecipeHandler>();

            RecipeContext recipeContext = new RecipeContext { RecipeStep = new RecipeStep { Name = "Module", Step = new XElement("SuperWiki") } };
            recipeContext.RecipeStep.Step.Add(new XAttribute("packageId", "Coevery.Module.SuperWiki"));
            recipeContext.RecipeStep.Step.Add(new XAttribute("repository", "test"));

            IFeatureManager featureManager = _container.Resolve<IFeatureManager>();
            IEnumerable<FeatureDescriptor> enabledFeatures = featureManager.GetEnabledFeatures();
            Assert.That(enabledFeatures.Count(), Is.EqualTo(0));
            moduleRecipeHandler.ExecuteRecipeStep(recipeContext);


            var availableFeatures = featureManager.GetAvailableFeatures().Where(x => x.Id == "SuperWiki").FirstOrDefault();
            Assert.That(availableFeatures.Id, Is.EqualTo("SuperWiki"));
            Assert.That(recipeContext.Executed, Is.True);
        }

        [Test]
        public void ExecuteRecipeStepNeedsNameTest() {
            _folders.Manifests.Add("SuperWiki", @"
Name: SuperWiki
Version: 1.0.3
CoeveryVersion: 1
Features:
    SuperWiki: 
        Description: My super wiki module for Coevery.
");

            ModuleRecipeHandler moduleRecipeHandler = _container.Resolve<ModuleRecipeHandler>();

            RecipeContext recipeContext = new RecipeContext { RecipeStep = new RecipeStep { Name = "Module", Step = new XElement("SuperWiki") } };
            recipeContext.RecipeStep.Step.Add(new XAttribute("repository", "test"));

            Assert.Throws(typeof (InvalidOperationException), () => moduleRecipeHandler.ExecuteRecipeStep(recipeContext));
        }

        [Test]
        public void ExecuteRecipeStepWithRepositoryAndVersionNotLatestTest() {
            _packagesInRepository.AddPublishedPackage(new PublishedPackage {
                Id = "Coevery.Module.SuperWiki",
                PackageType = DefaultExtensionTypes.Module,
                Title = "SuperWiki",
                Version = "1.0.3",
                IsLatestVersion = true,
            });
            _packagesInRepository.AddPublishedPackage(new PublishedPackage {
                Id = "Coevery.Module.SuperWiki",
                PackageType = DefaultExtensionTypes.Module,
                Title = "SuperWiki",
                Version = "1.0.2",
                IsLatestVersion = false,
            });

            ModuleRecipeHandler moduleRecipeHandler = _container.Resolve<ModuleRecipeHandler>();

            RecipeContext recipeContext = new RecipeContext { RecipeStep = new RecipeStep { Name = "Module", Step = new XElement("SuperWiki") } };
            recipeContext.RecipeStep.Step.Add(new XAttribute("packageId", "Coevery.Module.SuperWiki"));
            recipeContext.RecipeStep.Step.Add(new XAttribute("repository", "test"));
            recipeContext.RecipeStep.Step.Add(new XAttribute("version", "1.0.2"));

            moduleRecipeHandler.ExecuteRecipeStep(recipeContext);

            var installedPackage = _packageManager.GetInstalledPackages().FirstOrDefault(info => info.ExtensionName == "Coevery.Module.SuperWiki");
            Assert.That(installedPackage, Is.Not.Null);
            Assert.That(installedPackage.ExtensionVersion, Is.EqualTo("1.0.2"));
            Assert.That(recipeContext.Executed, Is.True);
        }

        internal class StubPackagingSourceManager : IPackagingSourceManager {
            private List<PublishedPackage> _publishedPackages = new List<PublishedPackage>();

            public IEnumerable<PackagingSource> GetSources() {
                return Enumerable.Empty<PackagingSource>();
            }

            public int AddSource(string feedTitle, string feedUrl) {
                throw new NotImplementedException();
            }

            public void RemoveSource(int id) {
                throw new NotImplementedException();
            }

            public IEnumerable<PackagingEntry> GetExtensionList(bool includeScreenshots, PackagingSource packagingSource = null, Func<IQueryable<PublishedPackage>, IQueryable<PublishedPackage>> query = null) {
                return query(_publishedPackages.AsQueryable()).Select(package => CreatePackagingEntry(package));
            }

            public int GetExtensionCount(PackagingSource packagingSource = null, Func<IQueryable<PublishedPackage>, IQueryable<PublishedPackage>> query = null) {
                throw new NotImplementedException();
            }

            public void AddPublishedPackage(PublishedPackage package) {
                _publishedPackages.Add(package);
            }

            private static PackagingEntry CreatePackagingEntry(PublishedPackage package) {
                return new PackagingEntry {
                    PackageId = package.Id,
                    Title = package.Title,
                    Version = package.Version,
                };
            }
        }

        internal class StubPackageManager : IPackageManager {
            private IList<PackageInfo> _installedPackages = new List<PackageInfo>();

            public IEnumerable<PackageInfo> GetInstalledPackages() {
                return _installedPackages;
            }

            public PackageData Harvest(string extensionName) {
                throw new NotImplementedException();
            }

            public PackageInfo Install(IPackage package, string location, string applicationPath) {
                return null;
            }

            public PackageInfo Install(string packageId, string version, string location, string applicationPath) {
                var package = new PackageInfo {
                    ExtensionName = packageId,
                    ExtensionVersion = version,
                };
                _installedPackages.Add(package);
                return package;
            }

            public void Uninstall(string packageId, string applicationPath) {
            }

            public ExtensionDescriptor GetExtensionDescriptor(IPackage package, string extensionType) {
                throw new NotImplementedException();
            }
        }

        internal class StubDataMigrationManager : IDataMigrationManager {
            public bool IsFeatureAlreadyInstalled(string feature) {
                return true;
            }

            public IEnumerable<string> GetFeaturesThatNeedUpdate() {
                return Enumerable.Empty<string>();
            }

            public void Update(string feature) {
            }

            public void Update(IEnumerable<string> features) {
            }

            public void Uninstall(string feature) {
            }
        }
    }
}