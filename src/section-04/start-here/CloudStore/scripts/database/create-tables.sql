CREATE TABLE "Products"
(
    Id            int identity (1,1)                  NOT NULL
        CONSTRAINT "PK_Products" PRIMARY KEY,
    [Name]        varchar(200)                        NOT NULL,
    Price         decimal(18, 2)                      NOT NULL,
    Category      varchar(200)                        NOT NULL,
    StockQuantity int                                 NOT NULL,
    CreatedAt     datetimeoffset default getutcdate() NOT NULL
    );  