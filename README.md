# MongoDBSessionStateProvider #

Proveedor personalizado de Session State basado en MongoDB.

Para hacerlo funcionar simplemente necesitamos añadir en el web.config la siguiente sección.

	<system.web>
	    <sessionState mode="Custom" customProvider="MongoSessionStateProvider">
	      <providers>
	        <add name="MongoSessionStateProvider" 
	             type="MongoDBSessionStateProvider.MongoDbSessionStateProvider" 
	             connectionString="MongoDB_ConnectionString"
	             dataBase="MongoDB_DatabaseName"
	             sessionCollection="MongoDB_SessionCollection"
	             writeExceptionsToEventLog="false" 
	             fsync="false" 
	             replicasToWrite="0" />
	 		</providers>
	    </sessionState>
	</system.web>


----------

Donde deberemos indicar los siguientes parámetros:

- **connectionStringName**: Nombre del parámetro de connectionStrings que tendrá la cadena de conexión a MongoDB.
- - **dataBaseName**: Nombre del parámetro de appSettings que tendrá el nombre de la base de datos de MongoDB donde se guardará el estado de la sesión.
- **sessionCollection**: Nombre del parámetro de appSettings que tendrá el nombre de la colección de MongoDB donde se guardará el estado de la sesión.


----------

		<appSettings>
			<add key="MongoDB_DatabaseName" value="MongoDatabase"/>
			<add key="MongoDB_SessionCollection" value="SessionState"/>
		</appSettings>
		<connectionStrings>
	  		<clear />
		  	<add name="MongoDB_ConnectionString" connectionString="mongodb://user:pwd@127.0.0.0:27017"/>
		</connectionStrings>

![](http://i.imgur.com/SFFihRZ.png)