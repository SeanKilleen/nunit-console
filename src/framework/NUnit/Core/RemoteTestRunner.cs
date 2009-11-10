// ***********************************************************************
// Copyright (c) 2007 Charlie Poole
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

namespace NUnit.Core
{
	using System;
    using System.Reflection;
	using System.Collections;
	using System.Diagnostics;

	/// <summary>
	/// RemoteTestRunner is tailored for use as the initial runner to
	/// receive control in a remote domain. It provides isolation for the return
	/// value by using a ThreadedTestRunner and for the events through use of
	/// an EventPump.
	/// </summary>
	public class RemoteTestRunner : ProxyTestRunner
	{
		/// <summary>
		/// Returns a RemoteTestRunner in the target domain. This method
		/// is used in the domain that wants to get a reference to 
		/// a RemoteTestRunnner and not in the test domain itself.
		/// </summary>
		/// <param name="targetDomain">AppDomain in which to create the runner</param>
		/// <param name="ID">Id for the new runner to use</param>
		/// <returns></returns>
        public static RemoteTestRunner CreateInstance(AppDomain targetDomain, int ID)
        {
#if CLR_2_0
            System.Runtime.Remoting.ObjectHandle oh = Activator.CreateInstance(
                targetDomain,
#else
			System.Runtime.Remoting.ObjectHandle oh = targetDomain.CreateInstance(
#endif
                Assembly.GetExecutingAssembly().FullName,
                typeof(RemoteTestRunner).FullName,
                false, BindingFlags.Default, null, new object[] { ID }, null, null, null);

            object obj = oh.Unwrap();
            Type type = obj.GetType();
            return (RemoteTestRunner)obj;
        }

		static Logger log = InternalTrace.GetLogger("RemoteTestRunner");

		#region Constructors
		public RemoteTestRunner() : this( 0 ) { }

		public RemoteTestRunner( int runnerID ) : base( runnerID ) { }
		#endregion

		#region Method Overrides
		public override bool Load(TestPackage package)
		{
			log.Info("Loading Test Package " + package.Name );

			// Initialize ExtensionHost if not already done
			if ( !CoreExtensions.Host.Initialized )
				CoreExtensions.Host.InitializeService();

			// Delayed creation of downstream runner allows us to
			// use a different runner type based on the package
			bool useThreadedRunner = package.GetSetting( "UseThreadedRunner", true );
			
			TestRunner runner = new SimpleTestRunner( this.runnerID );
			if ( useThreadedRunner )
				runner = new ThreadedTestRunner( runner );

			this.TestRunner = runner;

			if( base.Load (package) )
			{
				log.Info("Loaded package successfully" );
				return true;
			}
			else
			{
				log.Info("Package load failed" );
				return false;
			}
		}

        public override void Unload()
        {
            log.Info("Unloading test package");
            base.Unload();
        }

        public override TestResult Run(ITestListener listener)
		{
			return Run( listener, TestFilter.Empty );
		}

		public override TestResult Run( ITestListener listener, TestFilter filter )
		{
            log.Debug("Run");

            QueuingEventListener queue = new QueuingEventListener();

			StartTextCapture( queue );

			using( EventPump pump = new EventPump( listener, queue.Events, true ) )
			{
				pump.Start();
				return base.Run( queue, filter );
			}
		}

		public override void BeginRun( ITestListener listener )
		{
			BeginRun( listener, TestFilter.Empty );
		}

		public override void BeginRun( ITestListener listener, TestFilter filter )
		{
            log.Debug("BeginRun");

			QueuingEventListener queue = new QueuingEventListener();

			StartTextCapture( queue );

			EventPump pump = new EventPump( listener, queue.Events, true);
			pump.Start(); // Will run till RunFinished is received
			// TODO: Make sure the thread is cleaned up if we abort the run
		
			base.BeginRun( queue, filter );
		}

		private void StartTextCapture( ITestListener queue )
		{
			TestContext.Out = new EventListenerTextWriter( queue, TestOutputType.Out );
			TestContext.Error = new EventListenerTextWriter( queue, TestOutputType.Error );
			TestContext.TraceWriter = new EventListenerTextWriter( queue, TestOutputType.Trace );
			TestContext.Tracing = true;
			TestContext.LogWriter = new EventListenerTextWriter( queue, TestOutputType.Log );
			TestContext.Logging = true;
		}
		#endregion

		private void CurrentDomain_DomainUnload(object sender, EventArgs e)
		{
			log.Debug(AppDomain.CurrentDomain.FriendlyName + " unloaded");
			InternalTrace.Flush();
		}
	}
}