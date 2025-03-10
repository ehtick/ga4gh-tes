﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Tes.Models;
using TesApi.Web;
using TesApi.Web.Options;
using TesApi.Web.Storage;

namespace TesApi.Tests.Storage
{
    [TestClass, TestCategory("Unit")]
    public class DefaultStorageAccessProviderTests
    {
        private DefaultStorageAccessProvider defaultStorageAccessProvider;
        private Mock<IAzureProxy> azureProxyMock;
        private StorageOptions storageOptions;
        private StorageAccountInfo storageAccountInfo;
        private const string DefaultStorageAccountName = "defaultstorage";
        private const string StorageAccountBlobEndpoint = $"https://{DefaultStorageAccountName}.blob.core.windows.net";

        [TestInitialize]
        public void Setup()
        {
            azureProxyMock = new Mock<IAzureProxy>();
            storageOptions = new StorageOptions() { DefaultAccountName = DefaultStorageAccountName, ExecutionsContainerName = StorageAccessProvider.TesExecutionsPathPrefix };
            var subscriptionId = Guid.NewGuid().ToString();
            storageAccountInfo = new StorageAccountInfo()
            {
                BlobEndpoint = new(StorageAccountBlobEndpoint),
                Name = DefaultStorageAccountName,
                Id = $"/subscriptions/{subscriptionId}/resourceGroups/mrg/providers/Microsoft.Storage/storageAccounts/{DefaultStorageAccountName}",
                SubscriptionId = subscriptionId
            };
            azureProxyMock.Setup(p => p.GetStorageAccountUserKeyAsync(It.IsAny<StorageAccountInfo>(), It.IsAny<CancellationToken>())).ReturnsAsync(GenerateRandomTestAzureStorageKey());
            azureProxyMock.Setup(p => p.GetStorageAccountInfoAsync(It.Is<string>(s => s.Equals(DefaultStorageAccountName)), It.IsAny<CancellationToken>())).ReturnsAsync(storageAccountInfo);
            var config = CommonUtilities.AzureCloud.AzureCloudConfig.ForUnitTesting().AzureEnvironmentConfig;
            defaultStorageAccessProvider = new DefaultStorageAccessProvider(NullLogger<DefaultStorageAccessProvider>.Instance, Options.Create(storageOptions), azureProxyMock.Object, config);
        }

        [DataTestMethod]
        [DataRow("script/foo.sh")]
        [DataRow("/script/foo.sh")]
        public async Task GetInternalTesTaskBlobUrlAsync_BlobPathIsProvided_ReturnsValidURLWithDefaultStorageAccountTesInternalContainerAndTaskId(
            string blobName)
        {
            var task = new TesTask { Name = "taskName", Id = Guid.NewGuid().ToString() };
            var uri = await defaultStorageAccessProvider.GetInternalTesTaskBlobUrlAsync(task, blobName, CancellationToken.None);

            Assert.IsNotNull(uri);
            Assert.AreEqual($"{StorageAccountBlobEndpoint}{StorageAccessProvider.TesExecutionsPathPrefix}/tasks/{task.Id}/{blobName.TrimStart('/')}", ToHostWithAbsolutePathOnly(uri));
        }

        private static string ToHostWithAbsolutePathOnly(Uri uri)
        {
            return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
        }

        [DataTestMethod]
        [DataRow("script/foo.sh")]
        [DataRow("/script/foo.sh")]
        public async Task GetInternalTesTaskBlobUrlAsync_BlobPathAndInternalPathPrefixIsProvided_ReturnsValidURLWithDefaultStorageAccountAndInternalPathPrefixAppended(
            string blobName)
        {
            var internalPathPrefix = "internalPathPrefix";

            var task = CreateNewTesTask();
            task.Resources = new TesResources();
            task.Resources.BackendParameters = new Dictionary<string, string>
            {
                { TesResources.SupportedBackendParameters.internal_path_prefix.ToString(), internalPathPrefix }
            };

            var uri = await defaultStorageAccessProvider.GetInternalTesTaskBlobUrlAsync(task, blobName, CancellationToken.None);

            Assert.IsNotNull(uri);
            Assert.AreEqual($"{StorageAccountBlobEndpoint}{StorageAccessProvider.TesExecutionsPathPrefix}/{internalPathPrefix}/{blobName.TrimStart('/')}", ToHostWithAbsolutePathOnly(uri));
        }

        private static TesTask CreateNewTesTask()
        {
            var task = new TesTask { Name = "taskName", Id = Guid.NewGuid().ToString() };
            return task;
        }


        [DataTestMethod]
        [DataRow("", $"{StorageAccountBlobEndpoint}{StorageAccessProvider.TesExecutionsPathPrefix}")]
        [DataRow("blob", $"{StorageAccountBlobEndpoint}{StorageAccessProvider.TesExecutionsPathPrefix}/blob")]
        public void GetInternalTesBlobUrlWithoutSasToken_BlobPathIsProvided_ExpectedUrl(string blobPath,
            string expectedUrl)
        {
            var url = defaultStorageAccessProvider.GetInternalTesBlobUrlWithoutSasToken(blobPath);

            Assert.AreEqual(expectedUrl, url.AbsoluteUri);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("blob")]
        public void GetInternalTesTaskBlobUrlWithoutSasToken_BlobPathAndInternalPathPrefixProvided_ExpectedUrl(string blobPath)
        {
            var internalPathPrefix = "internalPathPrefix";
            var task = CreateNewTesTask();
            task.Resources = new()
            {
                BackendParameters = new()
                {
                    { TesResources.SupportedBackendParameters.internal_path_prefix.ToString(), internalPathPrefix }
                }
            };

            var blobPathInUrl = (!string.IsNullOrEmpty(blobPath)) ? $"/{blobPath}" : string.Empty;

            var expectedUrl = $"{StorageAccountBlobEndpoint}{StorageAccessProvider.TesExecutionsPathPrefix}/{internalPathPrefix}{blobPathInUrl}";

            var url = defaultStorageAccessProvider.GetInternalTesTaskBlobUrlWithoutSasToken(task, blobPath);

            Assert.AreEqual(expectedUrl, url.AbsoluteUri);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("blob")]
        public void GetInternalTesTaskBlobUrlWithoutSasToken_BlobPathAndNoInternalPathPrefixProvided_ExpectedUrl(string blobPath)
        {
            var task = CreateNewTesTask();

            var blobPathInUrl = (!string.IsNullOrEmpty(blobPath)) ? $"/{blobPath}" : string.Empty;

            var expectedUrl = $"{StorageAccountBlobEndpoint}{StorageAccessProvider.TesExecutionsPathPrefix}/tasks/{task.Id}{blobPathInUrl}";

            var url = defaultStorageAccessProvider.GetInternalTesTaskBlobUrlWithoutSasToken(task, blobPath);

            Assert.AreEqual(expectedUrl, url.AbsoluteUri);
        }

        private static Azure.Storage.Blobs.Models.UserDelegationKey GenerateRandomTestAzureStorageKey()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            var length = 64;
            var random = new Random();
            var result = new StringBuilder(length);

            for (var i = 0; i < length; i++)
            {
                result.Append(chars[random.Next(chars.Length)]);
            }

            return GenerateTestAzureStorageKey(result.ToString());
        }

        internal static Azure.Storage.Blobs.Models.UserDelegationKey GenerateTestAzureStorageKey(string value)
        {
            return Create(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "blob", "2018-11-09", value);

            static Azure.Storage.Blobs.Models.UserDelegationKey Create(string signedObjectId, string signedTenantId, DateTimeOffset signedStartsOn, DateTimeOffset signedExpiresOn, string signedService, string signedVersion, string value)
            {
                ArgumentNullException.ThrowIfNull(signedObjectId);
                ArgumentNullException.ThrowIfNull(signedTenantId);
                ArgumentNullException.ThrowIfNull(signedService);
                ArgumentNullException.ThrowIfNull(signedVersion);
                ArgumentNullException.ThrowIfNull(value);

                return (Azure.Storage.Blobs.Models.UserDelegationKey)typeof(Azure.Storage.Blobs.Models.UserDelegationKey)
                    .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, types: [typeof(string), typeof(string), typeof(DateTimeOffset), typeof(DateTimeOffset), typeof(string), typeof(string), typeof(string)])
                    ?.Invoke([signedObjectId, signedTenantId, signedStartsOn, signedExpiresOn, signedService, signedVersion, value]) ?? throw new InvalidOperationException();
            }
        }

    }
}
