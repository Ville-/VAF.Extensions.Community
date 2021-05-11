﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using MFiles.VAF.Extensions;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using MFilesAPI;
using System.IO;

namespace MFiles.VAF.Extensions.Tests.ExtensionMethods.ObjVerEx
{
	[TestClass]
	public class CreateCopy
		: TestBaseWithVaultMock
	{
		static int count = 0;
		protected override Mock<Vault> GetVaultMock()
		{
			var vaultMock = base.GetVaultMock();

			var objectOperationsMock = new Mock<VaultObjectOperations>();
			objectOperationsMock.Setup
			(
				m => m.GetObjectPermissions(It.IsAny<ObjVer>())
			)
			.Returns(new Mock<ObjectVersionPermissions>().Object);
			vaultMock.Setup(m => m.ObjectOperations).Returns(objectOperationsMock.Object);

			return vaultMock;
		}

		protected virtual Mock<ObjectVersionAndProperties> CreateObjectVersionAndPropertiesMock
		(
			Vault vault,
			int objectType = 0,
			PropertyValues propertyValues = null
		)
		{
			// Sanity.
			propertyValues = propertyValues ?? new PropertyValues();

			// Create a new object version.
			var objVer = new ObjVer();
			objVer.SetIDs(objectType, ++count, 1);

			// Create the object version mock.
			var objectVersionMock = new Mock<ObjectVersion>();
			objectVersionMock.Setup(m => m.ObjVer).Returns(objVer);

			// Create the mock for the object version and properties.
			var objectVersionAndPropertiesMock = new Mock<ObjectVersionAndProperties>();
			objectVersionAndPropertiesMock.SetupAllProperties();
			objectVersionAndPropertiesMock.Setup(m => m.ObjVer).Returns(objVer);
			objectVersionAndPropertiesMock.Setup(m => m.Vault).Returns(vault);
			objectVersionAndPropertiesMock.Setup(m => m.VersionData).Returns(objectVersionMock.Object);
			objectVersionAndPropertiesMock.Setup(m => m.Properties).Returns(propertyValues);
			return objectVersionAndPropertiesMock;
		}

		internal virtual Mock<IObjectCopyCreator> GetObjectCopyCreatorMock()
		{
			var objectCopyCreatorMock = new Mock<IObjectCopyCreator>();
			objectCopyCreatorMock
				.Setup
				(
					m => m.CreateObject
					(
						It.IsAny<Vault>(),
						It.IsAny<int>(),
						It.IsAny<PropertyValues>(),
						It.IsAny<SourceObjectFiles>(),
						It.IsAny<bool>(),
						It.IsAny<bool>(),
						It.IsAny<AccessControlList>()
					)
				)
				.Returns(
				(
					Vault vault,
					int objectType,
					PropertyValues propertyValues,
					SourceObjectFiles sourceObjectFiles,
					bool singleFileDocument,
					bool checkIn,
					AccessControlList accessControlList
				) => this.CreateObjectVersionAndPropertiesMock(vault, objectType, propertyValues).Object);
			return objectCopyCreatorMock;
		}

		protected Common.ObjVerEx CreateSourceObject
		(
			params Tuple<int, MFDataType, object>[] propertyValues
		)
		{
			return this.CreateSourceObjectWithPropertyValues
			(
				(propertyValues ?? new Tuple<int, MFDataType, object>[0])
					.Select(t =>
					{
						var pv = new PropertyValue() { PropertyDef = t.Item1 };
						pv.TypedValue.SetValue(t.Item2, t.Item3);
						return pv;
					})
					.ToArray()
			);
		}

		protected Common.ObjVerEx CreateSourceObjectWithPropertyValues
		(
			params PropertyValue[] propertyValues
		)
		{
			// Create our mock objects.
			var vaultMock = this.GetVaultMock();

			// Create the property values collection.
			var pvs = new PropertyValues();
			foreach (var pv in propertyValues ?? new PropertyValue[0])
			{
				pvs.Add(-1, pv);
			}

			// Create the source object itself.
			return new VAF.Common.ObjVerEx
			(
				vaultMock.Object,
				this.CreateObjectVersionAndPropertiesMock
				(
					vaultMock.Object,
					propertyValues: pvs
				).Object
			);
		}

		[TestMethod]
		[ExpectedException(typeof(ArgumentNullException))]
		public void NullObjVerExThrows()
		{
			((Common.ObjVerEx)null).CreateCopy(objectCopyOptions: new ObjectCopyOptions());
		}

		[TestMethod]
		public void CopyIsNotNull()
		{
			// Create our mock objects.
			var objectCopyCreatorMock = this.GetObjectCopyCreatorMock();

			// Create our source object.
			var sourceObject = this.CreateSourceObject();

			// Execute.
			var copy = sourceObject.CreateCopy(objectCopyCreator: objectCopyCreatorMock.Object);

			// Target should not be null.
			Assert.IsNotNull(copy);
			Assert.IsNotNull(copy.Properties);
		}

		[TestMethod]
		public void SourcePropertiesCopied()
		{
			// Create our mock objects.
			var objectCopyCreatorMock = this.GetObjectCopyCreatorMock();

			// Create our source object.
			var sourceObject = this.CreateSourceObject
			(
				new Tuple<int, MFDataType, object>
				(
					(int)MFBuiltInPropertyDef.MFBuiltInPropertyDefClass,
					MFDataType.MFDatatypeLookup,
					123
				),
				new Tuple<int, MFDataType, object>
				(
					(int)MFBuiltInPropertyDef.MFBuiltInPropertyDefNameOrTitle,
					MFDataType.MFDatatypeText,
					"hello world"
				)
			);

			// Execute.
			var copy = sourceObject.CreateCopy(objectCopyCreator: objectCopyCreatorMock.Object);

			// Properties should be correct.
			Assert.AreEqual(2, copy.Properties.Count);
			Assert.AreEqual((int)MFBuiltInPropertyDef.MFBuiltInPropertyDefClass, copy.Properties[1].PropertyDef);
			Assert.AreEqual(123, copy.Properties[1].TypedValue.GetLookupID());
			Assert.AreEqual((int)MFBuiltInPropertyDef.MFBuiltInPropertyDefNameOrTitle, copy.Properties[2].PropertyDef);
			Assert.AreEqual("hello world", copy.Properties[2].TypedValue.DisplayValue);
		}

		// When RemoveSystemProperties = true (default), the system properties should be removed.
		[TestMethod]
		public void SourcePropertiesCopied_SystemPropertiesRemoved()
		{
			// Create our mock objects.
			var objectCopyCreatorMock = this.GetObjectCopyCreatorMock();

			// Create our source object.
			var sourceObject = this.CreateSourceObject
			(
				new Tuple<int, MFDataType, object>
				(
					(int)MFBuiltInPropertyDef.MFBuiltInPropertyDefLastModified,
					MFDataType.MFDatatypeTimestamp,
					new Timestamp()
				)
			);

			// Execute.
			var copy = sourceObject.CreateCopy
			(
				new ObjectCopyOptions()
				{
					RemoveSystemProperties = true
				},
				objectCopyCreator: objectCopyCreatorMock.Object
			);

			// Properties should be correct.
			Assert.AreEqual(0, copy.Properties.Count);
		}

		// When RemoveSystemProperties = false, the system properties should not be removed.
		[TestMethod]
		public void SourcePropertiesCopied_SystemPropertiesRetained()
		{
			// Create our mock objects.
			var objectCopyCreatorMock = this.GetObjectCopyCreatorMock();

			// Create our source object.
			var sourceObject = this.CreateSourceObject
			(
				new Tuple<int, MFDataType, object>
				(
					(int)MFBuiltInPropertyDef.MFBuiltInPropertyDefLastModified,
					MFDataType.MFDatatypeTimestamp,
					new Timestamp()
				)
			);

			// Execute.
			var copy = sourceObject.CreateCopy
			(
				new ObjectCopyOptions()
				{
					RemoveSystemProperties = false
				},
				objectCopyCreator: objectCopyCreatorMock.Object
			);

			// Properties should be correct.
			Assert.AreEqual(1, copy.Properties.Count);
		}

		[TestMethod]
		public void CorrectTargetType()
		{
			// Create our mock objects.
			var objectCopyCreatorMock = this.GetObjectCopyCreatorMock();

			// Create our source object.
			var sourceObject = this.CreateSourceObject();

			// Execute.
			var copy = sourceObject.CreateCopy
			(
				objectCopyOptions: new ObjectCopyOptions()
				{
					TargetObjectType = 123 // Not a document.
				},
				objectCopyCreator: objectCopyCreatorMock.Object
			);

			// Source was a document, but we overrode that in the creation.
			Assert.AreEqual(123, copy.ObjVer.Type);
		}

		[TestMethod]
		public void SingleFileDocumentSetToFalseForNonDocumentObjects()
		{
			// Create our mock objects.
			var objectCopyCreatorMock = this.GetObjectCopyCreatorMock();

			// Create our source object.
			var sourceObject = this.CreateSourceObject();

			// Execute.
			var copy = sourceObject.CreateCopy
			(
				objectCopyOptions: new ObjectCopyOptions()
				{
					TargetObjectType = 123 // Not a document.
				},
				objectCopyCreator: objectCopyCreatorMock.Object
			);

			// SFD = false.
			Assert.AreEqual(1, copy.Properties.Count);
			Assert.AreEqual((int)MFBuiltInPropertyDef.MFBuiltInPropertyDefSingleFileObject, copy.Properties[1].PropertyDef);
			Assert.AreEqual(false, copy.Properties[1].TypedValue.Value);
		}

		[TestMethod]
		public void PropertyValueInstructionsAreApplied()
		{
			// Create our mock objects.
			var objectCopyCreatorMock = this.GetObjectCopyCreatorMock();

			// Create our source object.
			var sourceObject = this.CreateSourceObject();

			// Create the property value instructions.
			var properties = new List<ObjectCopyOptions.PropertyValueInstruction>
			{
				new ObjectCopyOptions.PropertyValueInstruction
				(
					ObjectCopyOptions.PropertyValueInstructionType.ReplaceOrAddPropertyValue,
					(int)MFBuiltInPropertyDef.MFBuiltInPropertyDefClass,
					MFDataType.MFDatatypeLookup,
					1234
				)
			};

			// Execute.
			var copy = sourceObject.CreateCopy
			(
				objectCopyOptions: new ObjectCopyOptions()
				{
					Properties = properties
				},
				objectCopyCreator: objectCopyCreatorMock.Object
			);

			// SFD = false.
			Assert.AreEqual(1, copy.Properties.Count);
			Assert.AreEqual((int)MFBuiltInPropertyDef.MFBuiltInPropertyDefClass, copy.Properties[1].PropertyDef);
			Assert.AreEqual(1234, copy.Properties[1].TypedValue.GetLookupID());
		}

		[TestMethod]
		public void CheckIn_IsCalledWhenTrue()
		{
			// Create our mock objects.
			var objectCopyCreatorMock = this.GetObjectCopyCreatorMock();
			var checkedIn = false;
			objectCopyCreatorMock
				.Setup(m => m.CheckIn(It.IsAny<Common.ObjVerEx>(), It.IsAny<string>(), It.IsAny<int>()))
				.Callback((Common.ObjVerEx objVerEx, string comments, int userId) =>
				{
					checkedIn = true;
				})
				.Verifiable();

			// Create our source object.
			var sourceObject = this.CreateSourceObject();

			// Execute.
			var copy = sourceObject.CreateCopy
			(
				objectCopyOptions: new ObjectCopyOptions()
				{
					CheckInObject = true
				},
				objectCopyCreator: objectCopyCreatorMock.Object
			);

			// Assert that checkin was called.
			objectCopyCreatorMock.Verify();
			Assert.AreEqual(true, checkedIn);
		}

		[TestMethod]
		public void CheckIn_CommentsAndUserIdCorrect()
		{
			// Create our mock objects.
			var objectCopyCreatorMock = this.GetObjectCopyCreatorMock();
			objectCopyCreatorMock
				.Setup(m => m.CheckIn(It.IsAny<Common.ObjVerEx>(), It.IsAny<string>(), It.IsAny<int>()))
				.Callback((Common.ObjVerEx objVerEx, string comments, int userId) =>
				{
					Assert.AreEqual("hello world", comments);
					Assert.AreEqual(1234, userId);
				})
				.Verifiable();

			// Create our source object.
			var sourceObject = this.CreateSourceObject();

			// Execute.
			var copy = sourceObject.CreateCopy
			(
				objectCopyOptions: new ObjectCopyOptions()
				{
					CheckInObject = true,
					CheckInComments = "hello world",
					CreatedByUserId = 1234
				},
				objectCopyCreator: objectCopyCreatorMock.Object
			);

			// Assert that checkin was called.
			objectCopyCreatorMock.Verify();
		}

		[TestMethod]
		public void CheckIn_IsNotCalledWhenFalse()
		{
			// Create our mock objects.
			var objectCopyCreatorMock = this.GetObjectCopyCreatorMock();
			var checkedIn = false;
			objectCopyCreatorMock
				.Setup(m => m.CheckIn(It.IsAny<Common.ObjVerEx>(), It.IsAny<string>(), It.IsAny<int>()))
				.Callback((Common.ObjVerEx objVerEx, string comments, int userId) =>
				{
					checkedIn = true;
				});

			// Create our source object.
			var sourceObject = this.CreateSourceObject();

			// Execute.
			var copy = sourceObject.CreateCopy
			(
				objectCopyOptions: new ObjectCopyOptions()
				{
					CheckInObject = false
				},
				objectCopyCreator: objectCopyCreatorMock.Object
			);

			// Assert that checkin was not called.
			Assert.AreEqual(false, checkedIn);
		}

	}
}