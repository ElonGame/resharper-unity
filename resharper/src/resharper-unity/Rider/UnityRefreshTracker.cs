﻿using System;
using System.Collections.Generic;
using JetBrains.Application.changes;
using JetBrains.Application.Threading;
using JetBrains.DataFlow;
using JetBrains.Platform.RdFramework;
using JetBrains.Platform.Unity.Model;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Host.Features;
using JetBrains.ReSharper.Host.Features.BackgroundTasks;
using JetBrains.Rider.Model;
using JetBrains.Threading;

namespace JetBrains.ReSharper.Plugins.Unity.Rider
{
    [SolutionComponent]
    public class UnityRefresher
    {
        private readonly IShellLocks myLocks;
        private readonly Lifetime myLifetime;
        private readonly ISolution mySolution;
        private readonly UnityPluginProtocolController myPluginProtocolController;
        private bool myIsRefreshing;

        public UnityRefresher(IShellLocks locks, Lifetime lifetime, ISolution solution, UnityPluginProtocolController pluginProtocolController)
        {
            myLocks = locks;
            myLifetime = lifetime;
            mySolution = solution;
            myPluginProtocolController = pluginProtocolController;
        }

        public bool IsRefreshing => myIsRefreshing;

        public void Refresh()
        {
            myLocks.AssertMainThread();
            if (myIsRefreshing) return;

            myIsRefreshing = true;
            var result = myPluginProtocolController.UnityModel?.Refresh.Start(RdVoid.Instance)?.Result;

            if (result == null)
            {
                myIsRefreshing = false;
                return;
            }
            
            var lifetimeDef = Lifetimes.Define(myLifetime);
            var solution = mySolution.GetProtocolSolution();
            var solFolder = mySolution.SolutionFilePath.Directory;
                
            mySolution.GetComponent<RiderBackgroundTaskHost>().AddNewTask(lifetimeDef.Lifetime, 
                RiderBackgroundTaskBuilder.Create().WithHeader("Refresh").AsIndeterminate().AsNonCancelable());
                        
            result.Advise(lifetimeDef.Lifetime, _ =>
            {
                try
                {
                    var list = new List<string> {solFolder.FullPath};
                    solution.FileSystemModel.RefreshPaths.Start(new RdRefreshRequest(list, true));
                }
                finally
                {
                    myIsRefreshing = false;
                    lifetimeDef.Terminate();
                }
            });
        }
    }

    [SolutionComponent]
    public class UnityRefreshTracker
    {
        public UnityRefreshTracker(Lifetime lifetime, ISolution solution, UnityRefresher refresher, ChangeManager changeManager)
        {
            var groupingEvent = solution.Locks.GroupingEvents.CreateEvent(lifetime, "UnityRefresherOnSaveEvent", TimeSpan.FromMilliseconds(500),
                Rgc.Invariant, ()=>refresher.Refresh());

            var protocolSolution = solution.GetProtocolSolution();
            protocolSolution.Editors.AfterDocumentInEditorSaved.Advise(lifetime, _ =>
            {
                if (refresher.IsRefreshing) return;
                groupingEvent.FireIncoming();
            });

            changeManager.Changed2.Advise(lifetime, args =>
            {
                var t = args.ChangeMap.GetChanges<ProjectModelChange>();
                if (t == null)
                    return;
                groupingEvent.FireIncoming();
            });
        }
    }
}