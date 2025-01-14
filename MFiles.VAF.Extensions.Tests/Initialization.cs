﻿using MFilesAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MFiles.VAF.Configuration.Logging;
using MFiles.VAF.Configuration.Logging.Targets;
using MFiles.VAF.Configuration.Logging.NLog;

namespace MFiles.VAF.Extensions.Tests
{
	[TestClass]
	public class Initialization
	{
		[AssemblyInitialize]
		public static void MyTestInitialize(TestContext testContext)
		{
			var layout = "${level}:\t${message}\t${logger}${onexception:${newline}${exception:format=ToString:innerformat=ToString:separator=\r\n}}";

			LogManager.Initialize(Moq.Mock.Of<Vault>());
			global::NLog.LogManager.Configuration = new NLog.Config.LoggingConfiguration();
			// Output some stuff to the standard output.
			// This means it's associated with each test; click a test to see the standard output (debug lines!)
			global::NLog.LogManager.Configuration.AddRule
			(
				global::NLog.LogLevel.Debug,
				global::NLog.LogLevel.Fatal,
				new SensitivityAwareAsyncTargetWrapper
				(
					new global::NLog.Targets.ColoredConsoleTarget()
					{
						AutoFlush = true,
						Layout = layout
					},
					LogSensitivity.Minimum,
					new SensitivityFlag[0]
				)
			);
			global::NLog.LogManager.ReconfigExistingLoggers();

		}
	}
}
