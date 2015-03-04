using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Provider;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.SessionState;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using MongoDBSessionStateProvider.Infrastructure;

namespace MongoDBSessionStateProvider
{
    public sealed class MongoDbSessionStateProvider : SessionStateStoreProviderBase
    {
        private string _applicationName;
        private string _collectionName;
        private string _connectionString;
        private ConnectionStringSettings _connectionStringSettings;
        private string _databaseName;
        private SessionStateSection _sessionStateSection;
        private WriteConcern _writeConcern;

        /// <summary>
        /// Takes as input the name of the provider and a NameValueCollection instance of configuration settings. This method is used to set property values for the provider instance, including implementation-specific values and options specified in the configuration file (Machine.config or Web.config).
        /// </summary>
        /// <param name="name"></param>
        /// <param name="config"></param>
        public override void Initialize(string name, NameValueCollection config)
        {
            DbC.Require(() => config != null, new ArgumentNullException("config").Message);
            if (String.IsNullOrEmpty(name) || String.IsNullOrWhiteSpace(name))
            {
                name = "MongoDbSessionStateProvider";
            }

            if (String.IsNullOrEmpty(config["description"]))
            {
                config.Remove("description");
                config.Add("description", "MongoDb Session State provider");
            }

            base.Initialize(name, config);

            GetApplicationName();
            GetSessionState();
            GetConnectionString(config);
            GetDatabaseName(config);
            GetCollectionName(config);
        }

        /// <summary>
        /// Takes as input the HttpContext instance for the current request and the Timeout value for the current session, and returns a new SessionStateStoreData object with an empty ISessionStateItemCollection object, an HttpStaticObjectsCollection collection, and the specified Timeout value. The HttpStaticObjectsCollection instance for the ASP.NET application can be retrieved using the GetSessionStaticObjects method.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            var sessionStateItemCollection = new SessionStateItemCollection();
            var httpStaticObjectsCollection = SessionStateUtility.GetSessionStaticObjects(context);
            return new SessionStateStoreData(sessionStateItemCollection, httpStaticObjectsCollection, timeout);
        }

        /// <summary>
        /// Takes as input the HttpContext instance for the current request, the SessionID value for the current request, and the lock identifier for the current request, and adds an uninitialized item to the session data store with an actionFlags value of InitializeItem.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="id"></param>
        /// <param name="timeout"></param>
        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            var writeConcernResult = InsertDocument(id, String.Empty, timeout, 1);
            if (!writeConcernResult.Ok)
            {
                throw new Exception(writeConcernResult.ErrorMessage);
            }
        }

        /// <summary>
        /// Frees any resources no longer in use by the session-state store provider.
        /// </summary>
        public override void Dispose()
        {
        }

        /// <summary>
        /// Takes as input the HttpContext instance for the current request and performs any cleanup required by your session-state store provider.
        /// </summary>
        /// <param name="context"></param>
        public override void EndRequest(HttpContext context)
        {
        }

        /// <summary>
        /// This method performs the same work as the GetItemExclusive method, except that it does not attempt to lock the session item in the data store. The GetItem method is called when the EnableSessionState attribute is set to ReadOnly.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="id"></param>
        /// <param name="locked"></param>
        /// <param name="lockAge"></param>
        /// <param name="lockId"></param>
        /// <param name="actions"></param>
        /// <returns></returns>
        public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(false, context, id, out locked, out lockAge, out lockId, out actions);
        }

        /// <summary>
        /// Takes as input the HttpContext instance for the current request and the SessionID value for the current request. Retrieves session values and information from the session data store and locks the session-item data at the data store for the duration of the request. The GetItemExclusive method sets several output-parameter values that inform the calling SessionStateModule about the state of the current session-state item in the data store.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="id"></param>
        /// <param name="locked"></param>
        /// <param name="lockAge"></param>
        /// <param name="lockId"></param>
        /// <param name="actions"></param>
        /// <returns></returns>
        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(true, context, id, out locked, out lockAge, out lockId, out actions);
        }

        /// <summary>
        /// Takes as input the HttpContext instance for the current request and performs any initialization required by your session-state store provider.
        /// </summary>
        /// <param name="context"></param>
        public override void InitializeRequest(HttpContext context)
        {
        }

        /// <summary>
        /// Takes as input the HttpContext instance for the current request, the SessionID value for the current request, and the lock identifier for the current request, and releases the lock on an item in the session data store. This method is called when the GetItem or GetItemExclusive method is called and the data store specifies that the requested item is locked, but the lock age has exceeded the ExecutionTimeout value. The lock is cleared by this method, freeing the item for use by other requests.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="id"></param>
        /// <param name="lockId"></param>
        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {
            var mongoServer = GetServer();
            MongoCollection mongoCollection = GetCollection(mongoServer);

            var query = Query.And(Query.EQ(Strings.ID, id),
                Query.EQ(Strings.APPLICATION_NAME, _applicationName),
                Query.EQ(Strings.LOCKID, (Int32)lockId));

            var update = Update.Set(Strings.LOCKED, false);
            update.Set(Strings.EXPIRES, DateTime.Now.AddMinutes(_sessionStateSection.Timeout.TotalMinutes).ToUniversalTime());

            mongoCollection.Update(query, update, _writeConcern);
        }

        /// <summary>
        /// Takes as input the HttpContext instance for the current request, the SessionID value for the current request, and the lock identifier for the current request, and deletes the session information from the data store where the data store item matches the supplied SessionID value, the current application, and the supplied lock identifier. This method is called when the Abandon method is called.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="id"></param>
        /// <param name="lockId"></param>
        /// <param name="item"></param>
        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            var query = Query.And(
                Query.EQ(Strings.ID, id),
                Query.EQ(Strings.LOCKID, (Int32)lockId));
            RemoveDocument(query);
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            var mongoServer = GetServer();
            var mongoCollection = GetCollection(mongoServer);

            var query = Query.And(Query.EQ(Strings.ID, id),
                Query.EQ(Strings.APPLICATION_NAME, _applicationName));

            var update = Update.Set(Strings.EXPIRES, DateTime.Now.AddMinutes(_sessionStateSection.Timeout.TotalMinutes).ToUniversalTime());
            mongoCollection.Update(query, update, _writeConcern);
        }

        /// <summary>
        /// Takes as input the HttpContext instance for the current request, the SessionID value for the current request, a SessionStateStoreData object that contains the current session values to be stored, the lock identifier for the current request, and a value that indicates whether the data to be stored is for a new session or an existing session.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="id"></param>
        /// <param name="item"></param>
        /// <param name="lockId"></param>
        /// <param name="newItem"></param>
        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {
            var serializeItems = Serialize((SessionStateItemCollection)item.Items);
            if (newItem)
            {
                InsertDocument(id, serializeItems, item.Timeout, 0);
            }
            else
            {
                UpdateDocument(id, serializeItems, item.Timeout, lockId);
            }
        }

        /// <summary>
        /// Takes as input a delegate that references the Session_OnEnd event defined in the Global.asax file. If the session-state store provider supports the Session_OnEnd event, a local reference to the SessionStateItemExpireCallback parameter is set and the method returns true; otherwise, the method returns false.
        /// </summary>
        /// <param name="expireCallback"></param>
        /// <returns></returns>
        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        private SessionStateStoreData Deserialize(HttpContext context, string serializedItems, int timeout)
        {
            using (var memoryStream = new MemoryStream(Convert.FromBase64String(serializedItems)))
            {
                var sessionItems = new SessionStateItemCollection();
                if (memoryStream.Length > 0)
                {
                    using (var reader = new BinaryReader(memoryStream))
                    {
                        sessionItems = SessionStateItemCollection.Deserialize(reader);
                    }
                }
                return new SessionStateStoreData(sessionItems, SessionStateUtility.GetSessionStaticObjects(context), timeout);
            }
        }

        private void GetApplicationName()
        {
            _applicationName = HostingEnvironment.ApplicationVirtualPath;
        }

        private MongoCollection<BsonDocument> GetCollection(MongoServer mongoServer)
        {
            return mongoServer.GetDatabase(_databaseName).GetCollection(_collectionName);
        }

        private void GetCollectionName(NameValueCollection config)
        {
            var sessionCollectionName = config["sessionCollection"];
            _collectionName = ConfigurationManager.AppSettings[sessionCollectionName];
        }

        private void GetConnectionString(NameValueCollection config)
        {
            var connectionStringName = config["connectionString"];
            _connectionStringSettings = ConfigurationManager.ConnectionStrings[connectionStringName];
            if (_connectionStringSettings == null || _connectionStringSettings.ConnectionString.Trim() == String.Empty)
            {
                //TODO: literal
                throw new ProviderException("Connection string cannot be blank.");
            }
            _connectionString = _connectionStringSettings.ConnectionString;
        }

        private BsonDocument GetCurrentSessionItem(string id)
        {
            var mongoServer = GetServer();
            var mongoCollection = GetCollection(mongoServer);

            var mongoQuery = Query.And(
                Query.EQ(Strings.ID, id),
                Query.EQ(Strings.APPLICATION_NAME, _applicationName));

            return mongoCollection.FindOneAs<BsonDocument>(mongoQuery);
        }

        private void GetDatabaseName(NameValueCollection config)
        {
            _databaseName = ConfigurationManager.AppSettings[config["dataBase"]];
        }

        private MongoServer GetServer()
        {
            var mongoClient = new MongoClient(_connectionString);
            return mongoClient.GetServer();
        }

        private void GetSessionState()
        {
            var configuration = WebConfigurationManager.OpenWebConfiguration(_applicationName);
            _sessionStateSection = (SessionStateSection)configuration.GetSection("system.web/sessionState");
        }

        private SessionStateStoreData GetSessionStoreItem(bool lockRecord, HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actionFlags)
        {
            SessionStateStoreData item = null;
            lockAge = TimeSpan.Zero;
            lockId = null;
            locked = false;
            actionFlags = 0;

            // DateTime to check if current session item is expired.
            // String to hold serialized SessionStateItemCollection.
            var serializedItems = String.Empty;
            // True if a record is found in the database.
            var foundRecord = false;
            // True if the returned session item is expired and needs to be deleted.
            var deleteData = false;
            // Timeout value from the data store.
            var timeout = 0;

            // lockRecord is true when called from GetItemExclusive and
            // false when called from GetItem.
            // Obtain a lock if possible. Ignore the record if it is expired.
            IMongoQuery mongoQuery;
            var now = DateTime.Now;

            if (lockRecord)
            {
                locked = LockDocument(id, now);
            }

            var currentSessionItem = GetCurrentSessionItem(id);
            if (currentSessionItem != null)
            {
                var expires = currentSessionItem[Strings.EXPIRES].ToUniversalTime();
                if (expires < now.ToUniversalTime())
                {
                    // The record was expired. Mark it as not locked.
                    locked = false;
                    // The session was expired. Mark the data for deletion.
                    deleteData = true;
                }
                else
                {
                    foundRecord = true;
                }

                serializedItems = currentSessionItem[Strings.SESSIONITEMS].AsString;
                lockId = currentSessionItem[Strings.LOCKID].AsInt32;
                lockAge = now.ToUniversalTime().Subtract(currentSessionItem[Strings.LOCKDATE].AsDateTime);
                actionFlags = (SessionStateActions)currentSessionItem[Strings.FLAGS].AsInt32;
                timeout = currentSessionItem[Strings.TIMEOUT].AsInt32;
            }

            // If the returned session item is expired, 
            // delete the record from the data source.
            if (deleteData)
            {
                var query = Query.And(
                    Query.EQ(Strings.APPLICATION_NAME, _applicationName),
                    Query.EQ(Strings.ID, id));
                RemoveDocument(query);
            }

            // The record was not found. Ensure that locked is false.
            if (!foundRecord)
            {
                locked = false;
            }

            // If the record was found and you obtained a lock, then set 
            // the lockId, clear the actionFlags,
            // and create the SessionStateStoreItem to return.
            if (foundRecord && !locked)
            {
                lockId = (int)lockId + 1;
                var update = Update.Set(Strings.LOCKID, (int)lockId);
                update.Set(Strings.FLAGS, 0);
                var query = Query.And(
                    Query.EQ(Strings.APPLICATION_NAME, _applicationName),
                    Query.And(Query.EQ(Strings.ID, id)));
                UpdateDocument(query, update);

                // If the actionFlags parameter is not InitializeItem, 
                // deserialize the stored SessionStateItemCollection.
                item = actionFlags == SessionStateActions.InitializeItem
                    ? CreateNewStoreData(context, (int)_sessionStateSection.Timeout.TotalMinutes)
                    : Deserialize(context, serializedItems, timeout);
            }

            return item;
        }

        private WriteConcernResult InsertDocument(string id, string serializeItems, int timeout, int flags)
        {
            var mongoServer = GetServer();
            var mongoCollection = GetCollection(mongoServer);

            var now = DateTime.Now;
            var bsonDocument = new BsonDocument
                               {
                                   {Strings.ID, id},
                                   {Strings.APPLICATION_NAME, _applicationName},
                                   {Strings.CREATED, now.ToUniversalTime()},
                                   {Strings.EXPIRES, now.AddMinutes(timeout).ToUniversalTime()},
                                   {Strings.LOCKDATE, now.ToUniversalTime()},
                                   {Strings.LOCKID, 0},
                                   {Strings.TIMEOUT, timeout},
                                   {Strings.LOCKED, false},
                                   {Strings.SESSIONITEMS, serializeItems},
                                   {Strings.FLAGS, flags}
                               };

            var mongoQueries = Query.And(
                Query.EQ(Strings.APPLICATION_NAME, _applicationName),
                Query.EQ(Strings.ID, id),
                Query.LT(Strings.EXPIRES, now.ToUniversalTime()));
            RemoveDocument(mongoQueries);

            return mongoCollection.Insert(bsonDocument, _writeConcern);
        }

        private bool LockDocument(string id, DateTime now)
        {
            var mongoServer = GetServer();
            var mongoCollection = GetCollection(mongoServer);

            var mongoQuery = Query.And(
                Query.EQ(Strings.ID, id),
                Query.EQ(Strings.APPLICATION_NAME, _applicationName),
                Query.EQ(Strings.LOCKED, false),
                Query.GT(Strings.EXPIRES, now.ToUniversalTime()));

            var update = Update.Set(Strings.LOCKED, true);
            update.Set(Strings.LOCKDATE, now.ToUniversalTime());
            var result = mongoCollection.Update(mongoQuery, update, _writeConcern);

            //DocumentsAffected == 0 == No record was updated because the record was locked or not found.
            return result.DocumentsAffected == 0;
        }

        private WriteConcernResult RemoveDocument(IMongoQuery query)
        {
            var mongoServer = GetServer();
            var mongoCollection = GetCollection(mongoServer);

            return mongoCollection.Remove(query, _writeConcern);
        }

        private string Serialize(SessionStateItemCollection items)
        {
            using (var memoryStream = new MemoryStream())
            using (var binaryWriter = new BinaryWriter(memoryStream))
            {
                if (items != null)
                {
                    items.Serialize(binaryWriter);
                }

                binaryWriter.Close();

                return Convert.ToBase64String(memoryStream.ToArray());
            }
        }

        private WriteConcernResult UpdateDocument(IMongoQuery query, IMongoUpdate update)
        {
            var mongoServer = GetServer();
            var mongoCollection = GetCollection(mongoServer);

            return mongoCollection.Update(query, update, _writeConcern);
        }

        private WriteConcernResult UpdateDocument(string id, string serializeItems, int timeout, object lockId)
        {
            var mongoServer = GetServer();
            var mongoCollection = GetCollection(mongoServer);

            var query = Query.And(
                Query.EQ(Strings.ID, id),
                Query.EQ(Strings.APPLICATION_NAME, _applicationName),
                Query.EQ(Strings.LOCKID, (Int32)lockId));

            var update = Update.Set(Strings.EXPIRES, DateTime.Now.AddMinutes(timeout).ToUniversalTime());
            update.Set(Strings.SESSIONITEMS, serializeItems);
            update.Set(Strings.LOCKED, false);

            return mongoCollection.Update(query, update, _writeConcern);
        }

        private static class Strings
        {
            internal const string APPLICATION_NAME = "ApplicationName";
            internal const string CREATED = "Created";
            internal const string EXPIRES = "Expires";
            internal const string FLAGS = "Flags";
            internal const string ID = "_id";
            internal const string LOCKDATE = "LockDate";
            internal const string LOCKED = "Locked";
            internal const string LOCKID = "LockId";
            internal const string SESSIONITEMS = "SessionItems";
            internal const string TIMEOUT = "Timeout";
        }
    }
}
