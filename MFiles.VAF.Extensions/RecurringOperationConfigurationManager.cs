﻿// ReSharper disable once CheckNamespace
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MFiles.VAF.AppTasks;
using MFiles.VAF.Common;
using MFiles.VAF.Extensions.ScheduledExecution;

namespace MFiles.VAF.Extensions
{
	/// <summary>
	/// Manages recurring operations that are configured via standard VAF configuration.
	/// </summary>
	/// <typeparam name="TSecureConfiguration"></typeparam>
	public class RecurringOperationConfigurationManager<TSecureConfiguration>
		: Dictionary<IRecurringOperationConfigurationAttribute, IRecurringOperation>
		where TSecureConfiguration : class, new()
	{
		protected ConfigurableVaultApplicationBase<TSecureConfiguration> VaultApplication { get; private set; }
		public RecurringOperationConfigurationManager(ConfigurableVaultApplicationBase<TSecureConfiguration> vaultApplication)
		{
			this.VaultApplication = vaultApplication
				?? throw new ArgumentNullException(nameof(vaultApplication));
		}
		/// <summary>
		/// Attempts to get the configured provider of how to repeat the task processing.
		/// </summary>
		/// <param name="queueId">The queue ID</param>
		/// <param name="taskType">The task type ID</param>
		/// <param name="recurringOperation">The configured provider, if available.</param>
		/// <returns><see langword="true"/> if the provider is available, <see langword="false"/> otherwise.</returns>
		public bool TryGetValue(string queueId, string taskType, out IRecurringOperation recurringOperation)
		{
			var key = this.Keys.FirstOrDefault(c => c.QueueID == queueId && c.TaskType == taskType);
			if (null == key)
			{
				recurringOperation = null;
				return false;
			}
			recurringOperation = this[key];
			return true;
		}

		/// <summary>
		/// Gets the next time that the task processor should run,
		/// if a repeating configuration is available.
		/// </summary>
		/// <param name="queueId">The queue ID</param>
		/// <param name="taskType">The task type ID</param>
		/// <returns>The datetime it should run, or null if not available.</returns>
		public DateTime? GetNextTaskProcessorExecution(string queueId, string taskType, DateTime? after = null)
		{
			return this.TryGetValue(queueId, taskType, out IRecurringOperation recurringOperation)
				? recurringOperation.GetNextExecution(after)
				: null;
		}

		/// <summary>
		/// Reads <paramref name="configuration"/> to populate <see cref="RecurringOperationConfiguration"/>
		/// </summary>
		public virtual void PopulateFromConfiguration(TSecureConfiguration configuration)
		{
			// Remove anything we have configured.
			this.Clear();

			// Sanity.
			if (null == configuration
				|| null == this.VaultApplication?.TaskQueueResolver
				|| null == this.VaultApplication?.TaskManager)
				return;

			// Attempt to find any scheduled operation configuration.
			var schedules = new List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>>();
			this.GetTaskProcessorConfiguration(configuration, out schedules);

			// If we have nothing to apply to then die.
			if (null == schedules || schedules.Count == 0)
				return;

			// Load all the processors.
			var dictionary = this.VaultApplication.TaskQueueResolver?
				.GetQueues()?
				.SelectMany
				(
					q => this.VaultApplication.TaskQueueResolver?
						.GetProcessors(q)
						.Select
						(
							p => new KeyValuePair<string, Processor>($"{q}-{p.Type}", p)
						)
				)?.ToDictionary
				(
					p => p.Key,
					p => p.Value
				) ?? new Dictionary<string, Processor>();

			// Iterate over each schedule and see whether we can apply it.
			foreach (var tuple in schedules)
			{
				// Load the key and the schedule.
				var key = $"{tuple.Item1.QueueID}-{tuple.Item1.TaskType}";
				var schedule = tuple.Item2;

				// Validate.
				if (false == dictionary.ContainsKey(key))
				{
					SysUtils.ReportToEventLog
					(
						$"Found configuration schedule for queue {tuple.Item1.QueueID} and type {tuple.Item1.TaskType}, but no task processors were registered with that combination.",
						System.Diagnostics.EventLogEntryType.Warning
					);
					continue;
				}

				// Make sure we have no duplicates.
				if (this.Keys.Count(k => k.QueueID == tuple.Item1.QueueID && k.TaskType == tuple.Item1.TaskType) > 0)
				{
					SysUtils.ReportToEventLog
					(
						$"Multiple configuration schedules found for queue {tuple.Item1.QueueID} and type {tuple.Item1.TaskType}.  Only the first loaded will be used.",
						System.Diagnostics.EventLogEntryType.Error
					);
					continue;
				}

				// Cancel any future executions.
				this.VaultApplication.TaskManager.CancelAllFutureExecutions
				(
					tuple.Item1.QueueID,
					tuple.Item1.TaskType,
					includeCurrentlyExecuting: false,
					vault: this.VaultApplication.PermanentVault
				);

				// If we don't have a schedule then stop.
				var nextExecution = schedule?.GetNextExecution();
				if (false == nextExecution.HasValue)
					continue;

				// If this is an interval-based one then run it now instead of in x minutes.
				if (schedule is WrappedTimeSpan)
					nextExecution = DateTime.UtcNow;

				// Add it to the dictionary.
				this.Add
				(
					tuple.Item1,
					tuple.Item2
				);

				// Schedule the next run.
				this.VaultApplication.TaskManager.AddTask
				(
					this.VaultApplication.PermanentVault,
					tuple.Item1.QueueID,
					tuple.Item1.TaskType,
					activationTime: nextExecution.Value
				);
			}
		}

		/// <summary>
		/// Retrieves any task processor scheduling/recurring configuration
		/// that is exposed via VAF configuration.
		/// </summary>
		/// <param name="input">The object containing the <paramref name="fieldInfo"/>.</param>
		/// <param name="fieldInfo">The field to retrieve the configuration from.</param>
		/// <param name="schedules">All configuration found relating to scheduled execution.</param>
		/// <param name="recurse">Whether to recurse down the object structure exposed.</param>
		protected virtual void GetTaskProcessorConfiguration
		(
			object input,
			FieldInfo fieldInfo,
			out List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>> schedules,
			bool recurse = true
		)
		{
			schedules = new List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>>();
			if (null == input)
				return;

			// Get the basic value.
			var value = fieldInfo.GetValue(input);

			// If it is enumerable then iterate over the contents and add.
			if (typeof(IEnumerable).IsAssignableFrom(fieldInfo.FieldType))
			{
				foreach (var item in (IEnumerable)value)
				{
					var a = new List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>>();
					this.GetTaskProcessorConfiguration(item, out a);
					schedules.AddRange(a);
				}
				return;
			}

			// Otherwise just add.
			this.GetTaskProcessorConfiguration(value, out schedules);

		}

		/// <summary>
		/// Retrieves any task processor scheduling/recurring configuration
		/// that is exposed via VAF configuration.
		/// </summary>
		/// <param name="input">The object containing the <paramref name="propertyInfo"/>.</param>
		/// <param name="propertyInfo">The property to retrieve the configuration from.</param>
		/// <param name="schedules">All configuration found relating to scheduled execution.</param>
		/// <param name="recurse">Whether to recurse down the object structure exposed.</param>
		protected virtual void GetTaskProcessorConfiguration
		(
			object input,
			PropertyInfo propertyInfo,
			out List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>> schedules,
			bool recurse = true
		)
		{
			schedules = new List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>>();
			if (null == input)
				return;

			// Get the basic value.
			var value = propertyInfo.GetValue(input);
			if (value == null)
				return;

			// If it is enumerable then iterate over the contents and add.
			if (typeof(IEnumerable).IsAssignableFrom(propertyInfo.PropertyType))
			{
				foreach (var item in (IEnumerable)value)
				{
					var a = new List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>>();
					this.GetTaskProcessorConfiguration(item, out a);
					schedules.AddRange(a);
				}
				return;
			}

			// Otherwise just add.
			this.GetTaskProcessorConfiguration(value, out schedules);

		}

		/// <summary>
		/// Retrieves any task processor scheduling/recurring configuration
		/// that is exposed via VAF configuration.
		/// </summary>
		/// <param name="input">The object containing the configuration.</param>
		/// <param name="schedules">All configuration found relating to scheduled execution.</param>
		/// <param name="recurse">Whether to recurse down the object structure exposed.</param>
		protected virtual void GetTaskProcessorConfiguration
		(
			object input,
			out List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>> schedules,
			bool recurse = true
		)
		{
			schedules = new List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>>();
			if (null == input)
				return;

			// Get all the fields marked with [DataMember].
			foreach (var f in input.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
			{
				// No data member attribute then die.
				if (0 == f.GetCustomAttributes(typeof(System.Runtime.Serialization.DataMemberAttribute), false).Length)
					continue;

				// If it has the recurring operation configuration then we want it.
				var recurringOperationConfigurationAttributes = f
					.GetCustomAttributes(false)
					.Where(a => a is IRecurringOperationConfigurationAttribute)
					.Cast<IRecurringOperationConfigurationAttribute>();
				foreach (var recurringOperationConfigurationAttribute in recurringOperationConfigurationAttributes)
				{
					if (null != recurringOperationConfigurationAttribute)
					{
						// Validate the field type.
						if (!recurringOperationConfigurationAttribute.ExpectedPropertyOrFieldType.IsAssignableFrom(f.FieldType))
						{
							SysUtils.ReportToEventLog
							(
								$"Found [{recurringOperationConfigurationAttribute.GetType().Name}] but field was not of type {recurringOperationConfigurationAttribute.ExpectedPropertyOrFieldType.FullName} (actual: {f.FieldType.FullName})",
								System.Diagnostics.EventLogEntryType.Warning
							);
							continue;
						}

						// Get the value and deal with timespans.
						var value = f.GetValue(input);
						if (null == value)
							continue;
						if (value.GetType() == typeof(TimeSpan))
							value = new WrappedTimeSpan((TimeSpan)value);

						// Validate that the value is a recurring operation.
						if (!typeof(IRecurringOperation).IsAssignableFrom(value.GetType()))
						{
							SysUtils.ReportToEventLog
							(
								$"Found [{recurringOperationConfigurationAttribute.GetType().Name}] but field was not of type IRecurringOperation (actual: {value.GetType().FullName})",
								System.Diagnostics.EventLogEntryType.Warning
							);
							continue;
						}

						// Add the schedule to the collection.
						schedules
						.Add
						(
							new Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>
							(
								recurringOperationConfigurationAttribute,
								value as IRecurringOperation
							)
						);
					}
				}

				// Can we recurse?
				if (recurse)
				{
					var a = new List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>>();
					this.GetTaskProcessorConfiguration(input, f, out a);
					schedules.AddRange(a);
				}
			}

			// Now do the same for properties.
			foreach (var p in input.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				// No data member attribute then die.
				if (0 == p.GetCustomAttributes(typeof(System.Runtime.Serialization.DataMemberAttribute), false).Length)
					continue;

				// If it has the scheduled operation configuration then we want it.
				var recurringOperationConfigurationAttributes = p
					.GetCustomAttributes(false)
					.Where(a => a is IRecurringOperationConfigurationAttribute)
					.Cast<IRecurringOperationConfigurationAttribute>();
				foreach (var recurringOperationConfigurationAttribute in recurringOperationConfigurationAttributes)
				{
					if (null != recurringOperationConfigurationAttribute)
					{
						// Validate the property type.
						if (!recurringOperationConfigurationAttribute.ExpectedPropertyOrFieldType.IsAssignableFrom(p.PropertyType))
						{
							SysUtils.ReportToEventLog
							(
								$"Found [{recurringOperationConfigurationAttribute.GetType().Name}] but property was not of type {recurringOperationConfigurationAttribute.ExpectedPropertyOrFieldType.FullName} (actual: {p.PropertyType.FullName})",
								System.Diagnostics.EventLogEntryType.Warning
							);
							continue;
						}

						// Get the value and deal with timespans.
						var value = p.GetValue(input);
						if (null == value)
							continue;
						if (value.GetType() == typeof(TimeSpan))
							value = new WrappedTimeSpan((TimeSpan)value);

						// Validate the type.
						if (!typeof(IRecurringOperation).IsAssignableFrom(value.GetType()))
						{
							SysUtils.ReportToEventLog
							(
								$"Found [{recurringOperationConfigurationAttribute.GetType().Name}] but property was not of type IRecurringOperation (actual: {value.GetType().FullName})",
								System.Diagnostics.EventLogEntryType.Warning
							);
							continue;
						}

						// Add the schedule to the collection.
						schedules.Add
						(
							new Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>
							(
								recurringOperationConfigurationAttribute,
								value as IRecurringOperation
							)
						);
					}
				}

				// Can we recurse?
				if (recurse)
				{
					var a = new List<Tuple<IRecurringOperationConfigurationAttribute, IRecurringOperation>>();
					this.GetTaskProcessorConfiguration(input, p, out a);
					schedules.AddRange(a);
				}
			}
		}
		internal class WrappedTimeSpan
			: IRecurringOperation
		{
			public TimeSpan TimeSpan { get; set; }
			public WrappedTimeSpan(TimeSpan timeSpan)
			{
				this.TimeSpan = timeSpan;
			}
			public static implicit operator TimeSpan(WrappedTimeSpan input)
				=> input?.TimeSpan ?? TimeSpan.Zero;
			public static implicit operator WrappedTimeSpan(TimeSpan input)
				=> new WrappedTimeSpan(input);

			public DateTime? GetNextExecution(DateTime? after = null)
			{
				return (after ?? DateTime.UtcNow).ToUniversalTime().Add(this.TimeSpan);
			}

			public string ToDashboardDisplayString()
			{
				return this.TimeSpan.ToDashboardDisplayString();
			}
		}
	}
}