// 
// UnityDebuggerSession.cs
//   based on IPhoneDebuggerSession.cs
//  
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
//       Lucas Meijer <lucas@unity3d.com>
//       Levi Bard <levi@unity3d.com>
// 
// Copyright (c) 2009 Novell, Inc. (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using Mono.Debugger;
using Mono.Debugging;
using Mono.Debugger.Soft;
using Mono.Debugging.Client;
using System.Threading;
using System.Diagnostics;
using System.IO;
using MonoDevelop.Core;
using System.Net.Sockets;
using System.Net;
using System.Collections;
using System.Collections.Generic;

namespace MonoDevelop.Debugger.Soft.Unity
{
	/// <summary>
	/// Debugger session for Unity scripting code
	/// </summary>
	public class UnitySoftDebuggerSession : RemoteSoftDebuggerSession
	{
		Process unityprocess;
		string unityPath;
		public const uint clientPort = 57432;
		
		public UnitySoftDebuggerSession ()
		{
			unityPath = Util.UnityLocation;
			
			Adaptor.BusyStateChanged += delegate(object sender, BusyStateEventArgs e) {
				SetBusyState (e);
			};
			MonoDevelop.Ide.IdeApp.Exiting += (sender,args) => EndSession();
		}
		
		protected override void OnRun (DebuggerStartInfo startInfo)
		{
			var dsi = (UnityDebuggerStartInfo) startInfo;
			StartUnity(dsi);
			StartListening(dsi);
		}

		/// <summary>
		/// Launch Unity
		/// </summary>
		void StartUnity (UnityDebuggerStartInfo dsi)
		{
			if (!Util.UnityLaunch)
				return; // Wait for remote connection
				
			if (unityprocess != null)
				throw new InvalidOperationException ("Unity already started");
				
			if (PropertyService.IsMac && Directory.Exists (unityPath)) {
				dsi.Arguments = string.Format ("-W '{0}' --args {1}", unityPath, dsi.Arguments);
				unityPath = ("open");
			}
			
			var psi = new ProcessStartInfo (unityPath)
			{
				Arguments = dsi.Arguments,
				UseShellExecute = false,
				WorkingDirectory = Path.GetDirectoryName (unityPath)
			};

			// Pass through environment
			foreach (DictionaryEntry env in Environment.GetEnvironmentVariables ()) {
				Console.WriteLine ("{0} = \"{1}\"", env.Key, env.Value);
				psi.EnvironmentVariables[(string)env.Key] = (string)env.Value;
			}
			foreach (var env in dsi.EnvironmentVariables) {
				Console.WriteLine ("{0} = \"{1}\"", env.Key, env.Value);
				psi.EnvironmentVariables[env.Key] = env.Value;
			}
			
			// Connect back to soft debugger client
			psi.EnvironmentVariables.Add ("MONO_ARGUMENTS",string.Format ("--debugger-agent=transport=dt_socket,address=127.0.0.1:{0},embedding=1", clientPort));
			psi.EnvironmentVariables.Add ("MONO_LOG_LEVEL","debug");
			
			unityprocess = Process.Start (psi);
			
			unityprocess.EnableRaisingEvents = true;
			unityprocess.Exited += delegate
			{
				EndSession ();
			};
		}
		
		protected override void EndSession ()
		{
			try {
				Ide.DispatchService.GuiDispatch (() =>
					Ide.IdeApp.Workbench.CurrentLayout = UnityProjectServiceExtension.EditLayout
				);
				EndUnityProcess ();
				base.EndSession ();
			} catch (Mono.Debugger.Soft.VMDisconnectedException) {
			} catch (ObjectDisposedException) {
			}
		}
		
		protected override void OnExit ()
		{
			try {
				EndUnityProcess ();
				base.OnExit ();
			} catch (Mono.Debugger.Soft.VMDisconnectedException) {
			} catch (ObjectDisposedException) {
			}
		}
		
		void EndUnityProcess ()
		{
			Detach ();
			unityprocess = null;
		}
	

		protected override void OnConnected ()
		{
			base.OnConnected ();
			Ide.DispatchService.GuiDispatch (() =>
				Ide.IdeApp.Workbench.CurrentLayout = "Debug"
			);
		}

		protected override void OnAttachToProcess (long processId)
		{
			if (UnitySoftDebuggerEngine.UnityPlayers.ContainsKey ((uint)processId)) {
				int port = (int)(56000 + (processId % 1000));
				PlayerConnection.PlayerInfo player = UnitySoftDebuggerEngine.UnityPlayers[(uint)processId];
				try {
					HandleConnection (VirtualMachineManager.Connect (new IPEndPoint (player.m_IPEndPoint.Address, port)));
				} catch (Exception ex) {
					throw new Exception (string.Format ("Unable to attach to {0}:{1}", player.m_IPEndPoint.Address, port), ex);
				}
				return;
			}
			base.OnAttachToProcess (processId);
		}

		protected override void OnDetach()
		{
			try {
				base.OnDetach();
			} catch (ObjectDisposedException) {
			}
		}
	}
}
