CREATE DATABASE CloudStore
    ON
    ( NAME = CloudStore_Data,
        FILENAME = '/var/opt/mssql/data/cloudstore.mdf',
        SIZE = 10,
        MAXSIZE = 50,
        FILEGROWTH = 5 )
    LOG ON
    ( NAME = CloudStore_Log,
        FILENAME = '/var/opt/mssql/data/cloudstore.ldf',
        SIZE = 5MB,
        MAXSIZE = 25MB,
        FILEGROWTH = 5MB ) ;
GO

USE master
GO
--- Replace <REPLACE_ME> with a real password
CREATE LOGIN CloudStoreAdmin WITH PASSWORD='djqkf8uvM832DQZ2mfc9-localonly'
GO

USE CloudStore
GO

CREATE USER CloudStoreAdmin FOR LOGIN CloudStoreAdmin;
ALTER ROLE db_datareader ADD MEMBER CloudStoreAdmin;
ALTER ROLE db_datawriter ADD MEMBER CloudStoreAdmin;
GO
