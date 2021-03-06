﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MoPlus.Interpreter.BLL.Config;
using MoPlus.Interpreter.BLL.Solutions;
using MoPlus.ViewModel.Solutions;
using MoPlus.ViewModel.Tests.Staging;

namespace MoPlus.ViewModel.Tests
{
    /// <summary>
    /// Helper class which makes it easier to make tests. Some methods do "magic" for making async methods sync, etc.
    /// </summary>
    partial class BaseTest
    {
        public void SaveSolution(SolutionViewModel solutionVM)
        {
            if (solutionVM == null)
            {
                throw new ArgumentNullException("solutionVM");
            }

            var solution = solutionVM.Solution;
            Assert.IsNotNull(solution, "Couldn't find Solution!");

            //solutionVM.UpdateCommand.Execute(null);
            solutionVM.SaveSolution();
            solutionVM.LoadSolution(solution, true);
        }

        public SolutionViewModel NewSolution(BuilderViewModel builder, DesignerViewModel designer, string solutionName, string solutionNamespace, string outputFilename, string company, string product, string version, 
            string solutionPath, string templatePath)
        {
            builder.ProcessNewCommand(builder.SolutionsFolder);
            var solutionVM = designer.SelectedItem as SolutionViewModel;
            Assert.IsNotNull(solutionVM, "Couldn't find SolutionViewModel");
            
            solutionVM.SolutionName = solutionName;
            solutionVM.Namespace = solutionNamespace;
            solutionVM.OutputSolutionFileName = outputFilename;
            solutionVM.CompanyName = company;
            solutionVM.ProductName = product;
            solutionVM.ProductVersion = version;
            solutionVM.SolutionPath = solutionPath;
            solutionVM.TemplatePath = templatePath;
            solutionVM.Update();
            // get the solution view model from the builder and return that instead of solutionVM
            Assert.AreNotEqual(0, builder.SolutionsFolder.Solutions.Count, "Couldn't find SolutionViewModel from builder");
            SolutionViewModel builderSolutionVM = builder.SolutionsFolder.Solutions[0];
            builderSolutionVM.SolutionPath = solutionPath;
            SaveSolution(builderSolutionVM);
            return builderSolutionVM;
        }

        public void NewDatabaseSource(BuilderViewModel builder, DesignerViewModel designer, SolutionViewModel solutionVM, string serverName, string databaseName, string templatePath)
        {
            solutionVM.SpecificationSourcesFolder.ProcessNewDatabaseSourceCommand();

            var newDBSource = new DatabaseSourceViewModel();
            var dbSource = designer.SelectedItem as DatabaseSourceViewModel ?? newDBSource;
            Assert.AreNotSame(dbSource, newDBSource, "Couldn't find database source");
            dbSource.DatabaseTypeCode = (int)DatabaseTypeCode.SqlServer;
            dbSource.SourceDbServerName = serverName;
            dbSource.SourceDbName = databaseName;
            dbSource.TemplatePath = templatePath;
            Assert.IsTrue(File.Exists(dbSource.TemplatePath), "Solution template not found!");
            dbSource.Order = 1;

            dbSource.Update();
            solutionVM.Update();
            solutionVM.SpecTemplatesFolder.LoadSpecTemplates(solutionVM.Solution);
            SaveSolution(solutionVM);
        }

        public ProjectViewModel CreateNewProject(SolutionViewModel solutionVM, DesignerViewModel solutionDesigner, string projectName, string projectNamespace, string dbServerName, string dbName, string templateFilename, string tags)
        {
            solutionVM.ProjectsFolder.ProcessNewProjectCommand();
            var newProject = new ProjectViewModel();
            var project = solutionDesigner.SelectedItem as ProjectViewModel ?? newProject;
            Assert.AreNotSame(project, newProject, "Couldn't find project");
            project.Name = projectName;
            project.Namespace = projectNamespace;
            project.DbServerName = dbServerName;
            project.DbName = dbName;
            project.TemplatePath = templateFilename;
            project.Tags = tags;

            Assert.IsTrue(project.IsValid, "Project has errors!");

            project.Update();
            solutionVM.Update();

            SaveSolution(solutionVM);
            solutionVM.ProjectsFolder.LoadProjects(solutionVM.Solution);
            return project;
        }

        public void ReferenceProjects(SolutionViewModel solution, ProjectViewModel project, params Guid[] referencedProjects)
        {
            foreach (var referencedProject in referencedProjects)
            {
                project.ProjectReferenceList.Add(new ProjectReference(project.ProjectID, referencedProject));
            }
            project.Update();
            SaveSolution(solution);
        }

        public void BuildSolution(SolutionViewModel solutionVM)
                                               {
            SaveSolution(solutionVM);
            DoAsync(solutionVM, () => solutionVM.BuildSolution(true));
            //SaveSolution(solutionVM);
        }

        private bool mInAsync;
        private string mAsyncError;
        private void DoAsync(SolutionViewModel solutionVM, Action action)
        {
            Assert.IsFalse(mInAsync, "Trying to start second async method. Not possible!");
            mInAsync = true;
            try
        {
            using (var resetEvent = new AutoResetEvent(false))
            {
                    var updated = new EventHandler((sender, args) => resetEvent.Set());
                solutionVM.Updated += updated;
                    action();
                    Assert.IsTrue(resetEvent.WaitOne(BaseTest.EventWaitTimeout), "Timeout waiting for async operation!");
                solutionVM.Updated -= updated;
            }
            }
            finally
            {
                mInAsync = false;
            }
            if (!String.IsNullOrWhiteSpace(mAsyncError))
            {
                var msg = mAsyncError;
                mAsyncError = null;
                throw new Exception("Error occurred during async operation: " + msg);
            }
        }

        public void UpdateOutputSolution(SolutionViewModel solutionVM)
        {
            DoAsync(solutionVM, () => solutionVM.UpdateOutputSolution());
        }
    }
}