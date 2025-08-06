using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.SQLMODELS;

namespace Prg_Moadian.FUNCTIONS
{
    public static class CL_ScriptUpdateDB
    {
        /// <summary>
        /// Update Database SQL Server via Script T-SQL
        /// </summary>
        public static void Go()
        {
            CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
            // Update Table Scripts

            #region CREATE AND ALTER TAXDTL

            try { dbms.DoExecuteSQL(@"CREATE TABLE [dbo].[TAXDTL]
                                          (
                                          [Taxid] [nvarchar] (22) COLLATE Arabic_CI_AS NULL,
                                          [Indatim] [bigint] NOT NULL,
                                          [Indati2m] [bigint] NOT NULL,
                                          [Inty] [int] NULL,
                                          [Inno] [nvarchar] (10) COLLATE Arabic_CI_AS NULL,
                                          [Irtaxid] [nvarchar] (22) COLLATE Arabic_CI_AS NULL,
                                          [Inp] [int] NULL,
                                          [Ins] [int] NULL,
                                          [Tins] [nvarchar] (14) COLLATE Arabic_CI_AS NULL,
                                          [Tob] [int] NULL,
                                          [Bid] [nvarchar] (12) COLLATE Arabic_CI_AS NULL,
                                          [Tinb] [nvarchar] (14) COLLATE Arabic_CI_AS NOT NULL,
                                          [Sbc] [nvarchar] (10) COLLATE Arabic_CI_AS NULL,
                                          [Bpc] [nvarchar] (10) COLLATE Arabic_CI_AS NULL,
                                          [Ft] [int] NULL,
                                          [Bpn] [nvarchar] (9) COLLATE Arabic_CI_AS NULL,
                                          [Scln] [nvarchar] (14) COLLATE Arabic_CI_AS NULL,
                                          [Scc] [nvarchar] (5) COLLATE Arabic_CI_AS NULL,
                                          [Crn] [nvarchar] (12) COLLATE Arabic_CI_AS NULL,
                                          [Billid] [nvarchar] (19) COLLATE Arabic_CI_AS NULL,
                                          [Tprdis] [decimal] (18, 0) NULL,
                                          [Tdis] [decimal] (18, 0) NULL,
                                          [Tadis] [decimal] (18, 0) NULL,
                                          [Tvam] [decimal] (18, 0) NULL,
                                          [Todam] [decimal] (18, 0) NULL,
                                          [Tbill] [decimal] (18, 0) NULL,
                                          [Setm] [decimal] (16, 3) NULL,
                                          [Cap] [numeric] (18, 0) NULL,
                                          [Insp] [decimal] (18, 0) NULL,
                                          [Tvop] [decimal] (18, 0) NULL,
                                          [Tax17] [decimal] (18, 0) NULL,
                                          [Sstid] [nvarchar] (13) COLLATE Arabic_CI_AS NULL,
                                          [Sstt] [nvarchar] (400) COLLATE Arabic_CI_AS NULL,
                                          [Mu] [nvarchar] (50) COLLATE Arabic_CI_AS NOT NULL,
                                          [Am] [decimal] (13, 8) NULL,
                                          [Fee] [decimal] (18, 8) NULL,
                                          [Cfee] [decimal] (15, 4) NULL,
                                          [Cut] [nvarchar] (3) COLLATE Arabic_CI_AS NULL,
                                          [Exr] [decimal] (18, 0) NULL,
                                          [Prdis] [decimal] (18, 0) NULL,
                                          [Dis] [decimal] (18, 0) NULL,
                                          [Adis] [decimal] (18, 0) NULL,
                                          [Vra] [decimal] (5, 2) NULL,
                                          [Vam] [decimal] (18, 0) NULL,
                                          [Odt] [nvarchar] (255) COLLATE Arabic_CI_AS NULL,
                                          [Odr] [decimal] (3, 2) NULL,
                                          [Odam] [decimal] (18, 0) NULL,
                                          [Olt] [nvarchar] (255) COLLATE Arabic_CI_AS NULL,
                                          [Olr] [decimal] (3, 2) NULL,
                                          [Olam] [decimal] (18, 0) NULL,
                                          [Consfee] [decimal] (18, 0) NULL,
                                          [Spro] [decimal] (18, 0) NULL,
                                          [Bros] [decimal] (18, 0) NULL,
                                          [Tcpbs] [decimal] (18, 0) NULL,
                                          [Cop] [decimal] (18, 0) NULL,
                                          [Vop] [decimal] (18, 0) NULL,
                                          [Bsrn] [nvarchar] (12) COLLATE Arabic_CI_AS NULL,
                                          [Tsstam] [decimal] (18, 0) NULL,
                                          [Iinn] [nvarchar] (9) COLLATE Arabic_CI_AS NULL,
                                          [Acn] [nvarchar] (14) COLLATE Arabic_CI_AS NULL,
                                          [Trmn] [nvarchar] (8) COLLATE Arabic_CI_AS NULL,
                                          [Trn] [nvarchar] (14) COLLATE Arabic_CI_AS NULL,
                                          [Pcn] [nvarchar] (16) COLLATE Arabic_CI_AS NULL,
                                          [Pid] [nvarchar] (12) COLLATE Arabic_CI_AS NULL,
                                          [Pdt] [numeric] (18, 0) NULL,
                                          [Cdcn] [nvarchar] (14) COLLATE Arabic_CI_AS NULL,
                                          [Cdcd] [int] NULL,
                                          [Tonw] [decimal] (16, 3) NULL,
                                          [Torv] [decimal] (18, 0) NULL,
                                          [Tocv] [decimal] (15, 4) NULL,
                                          [Nw] [decimal] (16, 3) NULL,
                                          [Ssrv] [decimal] (18, 0) NULL,
                                          [Sscv] [decimal] (15, 4) NULL,
                                          [Pmt] [int] NULL,
                                          [PV] [decimal] (18, 0) NULL,
                                          [IDD] [int] NOT NULL,
                                          [CRT] [datetime] NULL CONSTRAINT [DF__TAXDTL__CRT__08EE8210] DEFAULT (getdate()),
                                          [UID] [nvarchar] (100) COLLATE Arabic_CI_AS NULL,
                                          [RefrenceNumber] [nvarchar] (100) COLLATE Arabic_CI_AS NOT NULL,
                                          [TheConfirmationReferenceId] [nvarchar] (100) COLLATE Arabic_CI_AS NULL,
                                          [TheError] [nvarchar] (4000) COLLATE Arabic_CI_AS NULL,
                                          [TheStatus] [nvarchar] (50) COLLATE Arabic_CI_AS NULL,
                                          [TheSuccess] [bit] NULL,
                                          [TheWarning] [nvarchar] (4000) COLLATE Arabic_CI_AS NULL,
                                          [ApiTypeSent] [bit] NULL,
                                          [SentTaxMemory] [nvarchar] (12) COLLATE Arabic_CI_AS NULL
                                          ) ON [PRIMARY] "); } catch { }

            try { dbms.DoExecuteSQL(@"ALTER TABLE [dbo].[TAXDTL] ADD CONSTRAINT [TAXDTLI] PRIMARY KEY CLUSTERED ([IDD]) ON [PRIMARY]"); } catch { }

            try
            {
                dbms.DoExecuteSQL(@"ALTER TABLE TAXDTL ALTER COLUMN TheError nvarchar(4000) NULL
                                        ALTER TABLE TAXDTL ALTER COLUMN Tinb NVARCHAR(14) NOT NULL
                                        ALTER TABLE TAXDTL ALTER COLUMN Indatim BIGINT NOT NULL
                                        ALTER TABLE TAXDTL ALTER COLUMN Indati2m BIGINT NOT NULL
                                        ALTER TABLE TAXDTL ALTER COLUMN Mu nvarchar(50) NOT NULL
                                        ALTER TABLE TAXDTL ALTER COLUMN RefrenceNumber nvarchar(100) NOT NULL
                                        ALTER TABLE TAXDTL ALTER COLUMN UID nvarchar(100) NULL
                                        ALTER TABLE TAXDTL ALTER COLUMN TheConfirmationReferenceId	nvarchar(100)
                                        ALTER TABLE TAXDTL ALTER COLUMN TheStatus nvarchar(50) NULL
                                        ALTER TABLE TAXDTL ALTER COLUMN TheSuccess bit	NULL
                                        ALTER TABLE TAXDTL ALTER COLUMN TheWarning nvarchar(4000)
                                        ALTER TABLE TAXDTL ALTER COLUMN Tins nvarchar(14)
                                        ALTER TABLE TAXDTL ADD ApiTypeSent bit	NULL");
            }
            catch { }

            try { dbms.DoExecuteSQL("ALTER TABLE TAXDTL ADD SentTaxMemory nvarchar(12)"); } catch { }

            try { dbms.DoExecuteSQL("ALTER TABLE TAXDTL ALTER COLUMN Tins nvarchar(14)"); } catch { }

            try
            {   // -- برای مشخص کردن اینکه به کدام سامانه ارسال شده"
                // [0 | False] = SandBox Testy
                // [1 | True] = Main
                dbms.DoExecuteSQL("ALTER TABLE TAXDTL ADD ApiTypeSent bit NULL");
            }
            catch { }

            try { dbms.DoExecuteSQL("ALTER TABLE dbo.TAXDTL ALTER COLUMN Vra DECIMAL(4, 2);"); } catch { }

            #endregion

            #region CREATE INVO_LST_EXTENDED > TCOD_VAHED_EXTENDED  > CHANGE CUST_HESAB
            {
                string script = @"CREATE TABLE [dbo].[INVO_LST_EXTENDED](
                                            	[mu] [nvarchar](8) NOT NULL,
                                            	[nw] [decimal](16, 3) NULL,
                                            	[cfee] [decimal](15, 4) NULL,
                                            	[cut] [nvarchar](3) NULL,
                                            	[exr] [decimal](18, 0) NULL,
                                            	[ssrv] [decimal](18, 0) NULL,
                                            	[sscv] [decimal](15, 4) NULL,
                                            	[vra] [decimal](3, 2) NOT NULL,
                                            	[odt] [nvarchar](255) NULL,
                                            	[odr] [decimal](3, 2) NULL,
                                            	[odam] [decimal](18, 0) NULL,
                                            	[olt] [nvarchar](255) NULL,
                                            	[olr] [decimal](3, 2) NULL,
                                            	[olam] [decimal](18, 0) NULL,
                                            	[consfee] [decimal](18, 0) NULL,
                                            	[spro] [decimal](18, 0) NULL,
                                            	[bros] [decimal](18, 0) NULL,
                                            	[tcpbs] [decimal](18, 0) NULL,
                                            	[cop] [decimal](18, 0) NULL,
                                            	[vop] [decimal](18, 0) NULL,
                                            	[bsrn] [nvarchar](12) NULL,
                                            	[CRT] [datetime] NULL,
                                            	[UID] [int] NULL
                                            ) ON [PRIMARY]
                                            GO
                                            SET ANSI_NULLS ON
                                            GO
                                            SET QUOTED_IDENTIFIER ON
                                            GO
                                            CREATE TABLE [dbo].[TCOD_VAHED_EXTENDED](
                                            	[IDD] [int] NOT NULL,
                                            	[NAME_MO] [nvarchar](50) NOT NULL,
                                            	[CRT] [datetime] NULL,
                                            	[UID] [int] NULL,
                                             CONSTRAINT [PK_TCOD_VAHED_EXTENDEDD] PRIMARY KEY CLUSTERED 
                                            (
                                            	[IDD] ASC
                                            )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
                                            ) ON [PRIMARY]
                                            GO
                                            ALTER TABLE [dbo].[INVO_LST_EXTENDED] ADD  DEFAULT (getdate()) FOR [CRT]
                                            GO
                                            ALTER TABLE [dbo].[TAXDTL] ADD  CONSTRAINT [DF__TAXDTL__CRT__08EE8210]  DEFAULT (getdate()) FOR [CRT]
                                            GO
                                            ALTER TABLE [dbo].[TCOD_VAHED_EXTENDED] ADD  DEFAULT (getdate()) FOR [CRT]
                                            GO
                                            SET ANSI_NULLS ON
                                            GO
                                            SET QUOTED_IDENTIFIER ON
                                            GO
                                            ALTER VIEW [dbo].[CUST_HESAB]
                                            AS
                                            SELECT RTRIM(CAST(N_KOL AS NVARCHAR))+'-'+RTRIM(CAST(NUMBER AS NVARCHAR))+'-'+RTRIM(CAST(TNUMBER AS NVARCHAR)) AS hes, NAME, ADDRESS, TEL, CODE_E, ECODE, PCODE, IYALAT, CITY, MCODEM, TOZIH, CUST_COD, MOBILE, Longitude, Latitude, ROUTE_NAME, OSTANID, SHAHRID,tob
                                            FROM dbo.TDETA_HES
                                            UNION
                                            SELECT RTRIM(CAST(N_KOL AS NVARCHAR))+'-'+RTRIM(CAST(NUMBER AS NVARCHAR))+'-'+RTRIM(CAST(TNUMBER AS NVARCHAR))+'-'+RTRIM(CAST(TNUMBER2 AS NVARCHAR)) AS hes, NAME, ADDRESS, TEL, CODE_E, ECODE, PCODE, IYALAT, CITY, MCODEM, TOZIH, CUST_COD, MOBILE, Longitude, Latitude, ROUTE_NAME, OSTANID, SHAHRID,tob
                                            FROM dbo.TDETA_HES2
                                            UNION
                                            SELECT RTRIM(CAST(N_KOL AS NVARCHAR))+'-'+RTRIM(CAST(NUMBER AS NVARCHAR))+'-'+RTRIM(CAST(TNUMBER AS NVARCHAR))+'-'+RTRIM(CAST(TNUMBER2 AS NVARCHAR))+'-'+RTRIM(CAST(TNUMBER3 AS NVARCHAR)) AS hes, NAME, ADDRESS, TEL, CODE_E, ECODE, PCODE, IYALAT, CITY, MCODEM, TOZIH, CUST_COD, MOBILE, Longitude, Latitude, ROUTE_NAME, OSTANID, SHAHRID,tob
                                            FROM dbo.TDETA_HES3
                                            UNION
                                            SELECT RTRIM(CAST(N_KOL AS NVARCHAR))+'-'+RTRIM(CAST(NUMBER AS NVARCHAR))+'-'+RTRIM(CAST(TNUMBER AS NVARCHAR))+'-'+RTRIM(CAST(TNUMBER2 AS NVARCHAR))+'-'+RTRIM(CAST(TNUMBER3 AS NVARCHAR))+'-'+RTRIM(CAST(TNUMBER4 AS NVARCHAR)) AS hes, NAME, ADDRESS, TEL, CODE_E, ECODE, PCODE, IYALAT, CITY, MCODEM, TOZIH, CUST_COD, MOBILE, Longitude, Latitude, ROUTE_NAME, OSTANID, SHAHRID,tob
                                            FROM dbo.TDETA_HES4
                                            GO";

                var commands = script.Split(new string[] { "GO\r\n", "GO ", "GO\t" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var cmdText in commands)
                {
                    if (!string.IsNullOrWhiteSpace(cmdText))
                    {
                        try { dbms.DoExecuteSQL(cmdText); } catch { }
                    }
                }
            }
            #endregion

            #region DROP > CREATE > INSERT TCOD_ARZ

            //DELETE DATA dbo.TCOD_ARZ
            try { dbms.DoExecuteSQL("DELETE FROM dbo.TCOD_ARZ"); } catch { }

            //CREATE : TCOD_ARZ
            try { dbms.DoExecuteSQL(@"CREATE TABLE [dbo].[TCOD_ARZ]
                                          (
                                          [Code] [nvarchar] (100) COLLATE Arabic_CI_AS NULL,
                                          [Title] [nvarchar] (100) COLLATE Arabic_CI_AS NULL,
                                          [ISOCode] [nvarchar] (100) COLLATE Arabic_CI_AS NULL,
                                          [CountryName] [nvarchar] (100) COLLATE Arabic_CI_AS NULL,
                                          [CRT] [datetime] NULL CONSTRAINT [DF__TCOD_ARZ__CRT__7DDD8D6E] DEFAULT (getdate()),
                                          [UID] [int] NULL
                                          ) ON [PRIMARY] "); } catch { }

            //INSERT TCOD_ARZ
            try { dbms.DoExecuteSQL(@"INSERT INTO dbo.TCOD_ARZ ([Code], [Title], [ISOCode], [CountryName])
                                           VALUES
                                           ( N'965', N'ADB Unit of Account', N'XUA', N'MEMBER COUNTRIES OF THE AFRICAN DEVELOPMENT BANK GROUP' ), 
                                           ( N'971', N'Afghani', N'AFN', N'AFGHANISTAN' ), 
                                           ( N'012', N'Algerian Dinar', N'DZD', N'ALGERIA' ), 
                                           ( N'032', N'Argentine Peso', N'ARS', N'ARGENTINA' ), 
                                           ( N'051', N'Armenian Dram', N'AMD', N'ARMENIA' ), 
                                           ( N'533', N'Aruban Florin', N'AWG', N'ARUBA' ), 
                                           ( N'036', N'Australian Dollar', N'AUD', N'AUSTRALIA' ), 
                                           ( N'036', N'Australian Dollar', N'AUD', N'TUVALU' ), 
                                           ( N'036', N'Australian Dollar', N'AUD', N'CHRISTMAS ISLAND' ), 
                                           ( N'036', N'Australian Dollar', N'AUD', N'COCOS (KEELING) ISLANDS (THE)' ), 
                                           ( N'036', N'Australian Dollar', N'AUD', N'KIRIBATI' ), 
                                           ( N'036', N'Australian Dollar', N'AUD', N'HEARD ISLAND AND McDONALD ISLANDS' ), 
                                           ( N'036', N'Australian Dollar', N'AUD', N'NAURU' ), 
                                           ( N'036', N'Australian Dollar', N'AUD', N'NORFOLK ISLAND' ), 
                                           ( N'944', N'Azerbaijan Manat', N'AZN', N'AZERBAIJAN' ), 
                                           ( N'044', N'Bahamian Dollar', N'BSD', N'BAHAMAS (THE)' ), 
                                           ( N'048', N'Bahraini Dinar', N'BHD', N'BAHRAIN' ), 
                                           ( N'764', N'Baht', N'THB', N'THAILAND' ), 
                                           ( N'590', N'Balboa', N'PAB', N'PANAMA' ), 
                                           ( N'052', N'Barbados Dollar', N'BBD', N'BARBADOS' ), 
                                           ( N'933', N'Belarusian Ruble', N'BYN', N'BELARUS' ), 
                                           ( N'084', N'Belize Dollar', N'BZD', N'BELIZE' ), 
                                           ( N'060', N'Bermudian Dollar', N'BMD', N'BERMUDA' ), 
                                           ( N'928', N'Bolívar Soberano', N'VES', N'VENEZUELA (BOLIVARIAN REPUBLIC OF)' ), 
                                           ( N'926', N'Bolívar Soberano', N'VED', N'VENEZUELA (BOLIVARIAN REPUBLIC OF)' ), 
                                           ( N'068', N'Boliviano', N'BOB', N'BOLIVIA (PLURINATIONAL STATE OF)' ), 
                                           ( N'955', N'Bond Markets Unit European Composite Unit (EURCO)', N'XBA', N'ZZ01_Bond Markets Unit European_EURCO' ), 
                                           ( N'956', N'Bond Markets Unit European Monetary Unit (E.M.U.-6)', N'XBB', N'ZZ02_Bond Markets Unit European_EMU-6' ), 
                                           ( N'958', N'Bond Markets Unit European Unit of Account 17 (E.U.A.-17)', N'XBD', N'ZZ04_Bond Markets Unit European_EUA-17' ), 
                                           ( N'957', N'Bond Markets Unit European Unit of Account 9 (E.U.A.-9)', N'XBC', N'ZZ03_Bond Markets Unit European_EUA-9' ), 
                                           ( N'986', N'Brazilian Real', N'BRL', N'BRAZIL' ), 
                                           ( N'096', N'Brunei Dollar', N'BND', N'BRUNEI DARUSSALAM' ), 
                                           ( N'975', N'Bulgarian Lev', N'BGN', N'BULGARIA' ), 
                                           ( N'108', N'Burundi Franc', N'BIF', N'BURUNDI' ), 
                                           ( N'132', N'Cabo Verde Escudo', N'CVE', N'CABO VERDE' ), 
                                           ( N'124', N'Canadian Dollar', N'CAD', N'CANADA' ), 
                                           ( N'136', N'Cayman Islands Dollar', N'KYD', N'CAYMAN ISLANDS (THE)' ), 
                                           ( N'952', N'CFA Franc BCEAO', N'XOF', N'BURKINA FASO' ), 
                                           ( N'952', N'CFA Franc BCEAO', N'XOF', N'CÔTE D''IVOIRE' ), 
                                           ( N'952', N'CFA Franc BCEAO', N'XOF', N'BENIN' ), 
                                           ( N'952', N'CFA Franc BCEAO', N'XOF', N'TOGO' ), 
                                           ( N'952', N'CFA Franc BCEAO', N'XOF', N'NIGER (THE)' ), 
                                           ( N'952', N'CFA Franc BCEAO', N'XOF', N'SENEGAL' ), 
                                           ( N'952', N'CFA Franc BCEAO', N'XOF', N'GUINEA-BISSAU' ), 
                                           ( N'952', N'CFA Franc BCEAO', N'XOF', N'MALI' ), 
                                           ( N'950', N'CFA Franc BEAC', N'XAF', N'GABON' ), 
                                           ( N'950', N'CFA Franc BEAC', N'XAF', N'CONGO (THE)' ), 
                                           ( N'950', N'CFA Franc BEAC', N'XAF', N'CAMEROON' ), 
                                           ( N'950', N'CFA Franc BEAC', N'XAF', N'CENTRAL AFRICAN REPUBLIC (THE)' ), 
                                           ( N'950', N'CFA Franc BEAC', N'XAF', N'CHAD' ), 
                                           ( N'950', N'CFA Franc BEAC', N'XAF', N'EQUATORIAL GUINEA' ), 
                                           ( N'953', N'CFP Franc', N'XPF', N'WALLIS AND FUTUNA' ), 
                                           ( N'953', N'CFP Franc', N'XPF', N'FRENCH POLYNESIA' ), 
                                           ( N'953', N'CFP Franc', N'XPF', N'NEW CALEDONIA' ), 
                                           ( N'152', N'Chilean Peso', N'CLP', N'CHILE' ), 
                                           ( N'963', N'Codes specifically reserved for testing purposes', N'XTS', N'ZZ06_Testing_Code' ), 
                                           ( N'170', N'Colombian Peso', N'COP', N'COLOMBIA' ), 
                                           ( N'174', N'Comorian Franc ', N'KMF', N'COMOROS (THE)' ), 
                                           ( N'976', N'Congolese Franc', N'CDF', N'CONGO (THE DEMOCRATIC REPUBLIC OF THE)' ), 
                                           ( N'977', N'Convertible Mark', N'BAM', N'BOSNIA AND HERZEGOVINA' ), 
                                           ( N'558', N'Cordoba Oro', N'NIO', N'NICARAGUA' ), 
                                           ( N'188', N'Costa Rican Colon', N'CRC', N'COSTA RICA' ), 
                                           ( N'192', N'Cuban Peso', N'CUP', N'CUBA' ), 
                                           ( N'203', N'Czech Koruna', N'CZK', N'CZECHIA' ), 
                                           ( N'270', N'Dalasi', N'GMD', N'GAMBIA (THE)' ), 
                                           ( N'208', N'Danish Krone', N'DKK', N'GREENLAND' ), 
                                           ( N'208', N'Danish Krone', N'DKK', N'DENMARK' ), 
                                           ( N'208', N'Danish Krone', N'DKK', N'FAROE ISLANDS (THE)' ), 
                                           ( N'807', N'Denar', N'MKD', N'NORTH MACEDONIA' ), 
                                           ( N'262', N'Djibouti Franc', N'DJF', N'DJIBOUTI' ), 
                                           ( N'930', N'Dobra', N'STN', N'SAO TOME AND PRINCIPE' ), 
                                           ( N'214', N'Dominican Peso', N'DOP', N'DOMINICAN REPUBLIC (THE)' ), 
                                           ( N'704', N'Dong', N'VND', N'VIET NAM' ), 
                                           ( N'951', N'East Caribbean Dollar', N'XCD', N'ANGUILLA' ), 
                                           ( N'951', N'East Caribbean Dollar', N'XCD', N'ANTIGUA AND BARBUDA' ), 
                                           ( N'951', N'East Caribbean Dollar', N'XCD', N'DOMINICA' ), 
                                           ( N'951', N'East Caribbean Dollar', N'XCD', N'SAINT VINCENT AND THE GRENADINES' ), 
                                           ( N'951', N'East Caribbean Dollar', N'XCD', N'SAINT KITTS AND NEVIS' ), 
                                           ( N'951', N'East Caribbean Dollar', N'XCD', N'SAINT LUCIA' ), 
                                           ( N'951', N'East Caribbean Dollar', N'XCD', N'MONTSERRAT' ), 
                                           ( N'951', N'East Caribbean Dollar', N'XCD', N'GRENADA' ), 
                                           ( N'818', N'Egyptian Pound', N'EGP', N'EGYPT' ), 
                                           ( N'222', N'El Salvador Colon', N'SVC', N'EL SALVADOR' ), 
                                           ( N'230', N'Ethiopian Birr', N'ETB', N'ETHIOPIA' ), 
                                           ( N'978', N'Euro', N'EUR', N'EUROPEAN UNION' ), 
                                           ( N'978', N'Euro', N'EUR', N'ESTONIA' ), 
                                           ( N'978', N'Euro', N'EUR', N'FINLAND' ), 
                                           ( N'978', N'Euro', N'EUR', N'FRANCE' ), 
                                           ( N'978', N'Euro', N'EUR', N'FRENCH GUIANA' ), 
                                           ( N'978', N'Euro', N'EUR', N'CYPRUS' ), 
                                           ( N'978', N'Euro', N'EUR', N'CROATIA' ), 
                                           ( N'978', N'Euro', N'EUR', N'ANDORRA' ), 
                                           ( N'978', N'Euro', N'EUR', N'AUSTRIA' ), 
                                           ( N'978', N'Euro', N'EUR', N'BELGIUM' ), 
                                           ( N'978', N'Euro', N'EUR', N'ÅLAND ISLANDS' ), 
                                           ( N'978', N'Euro', N'EUR', N'SAN MARINO' ), 
                                           ( N'978', N'Euro', N'EUR', N'GUADELOUPE' ), 
                                           ( N'978', N'Euro', N'EUR', N'FRENCH SOUTHERN TERRITORIES (THE)' ), 
                                           ( N'978', N'Euro', N'EUR', N'GERMANY' ), 
                                           ( N'978', N'Euro', N'EUR', N'GREECE' ), 
                                           ( N'978', N'Euro', N'EUR', N'HOLY SEE (THE)' ), 
                                           ( N'978', N'Euro', N'EUR', N'IRELAND' ), 
                                           ( N'978', N'Euro', N'EUR', N'ITALY' ), 
                                           ( N'978', N'Euro', N'EUR', N'MALTA' ), 
                                           ( N'978', N'Euro', N'EUR', N'MARTINIQUE' ), 
                                           ( N'978', N'Euro', N'EUR', N'MAYOTTE' ), 
                                           ( N'978', N'Euro', N'EUR', N'LATVIA' ), 
                                           ( N'978', N'Euro', N'EUR', N'LITHUANIA' ), 
                                           ( N'978', N'Euro', N'EUR', N'LUXEMBOURG' ), 
                                           ( N'978', N'Euro', N'EUR', N'MONACO' ), 
                                           ( N'978', N'Euro', N'EUR', N'MONTENEGRO' ), 
                                           ( N'978', N'Euro', N'EUR', N'NETHERLANDS (THE)' ), 
                                           ( N'978', N'Euro', N'EUR', N'PORTUGAL' ), 
                                           ( N'978', N'Euro', N'EUR', N'SAINT MARTIN (FRENCH PART)' ), 
                                           ( N'978', N'Euro', N'EUR', N'SAINT PIERRE AND MIQUELON' ), 
                                           ( N'978', N'Euro', N'EUR', N'RÉUNION' ), 
                                           ( N'978', N'Euro', N'EUR', N'SAINT BARTHÉLEMY' ), 
                                           ( N'978', N'Euro', N'EUR', N'SLOVAKIA' ), 
                                           ( N'978', N'Euro', N'EUR', N'SLOVENIA' ), 
                                           ( N'978', N'Euro', N'EUR', N'SPAIN' ), 
                                           ( N'238', N'Falkland Islands Pound', N'FKP', N'FALKLAND ISLANDS (THE) [MALVINAS]' ), 
                                           ( N'242', N'Fiji Dollar', N'FJD', N'FIJI' ), 
                                           ( N'348', N'Forint', N'HUF', N'HUNGARY' ), 
                                           ( N'936', N'Ghana Cedi', N'GHS', N'GHANA' ), 
                                           ( N'292', N'Gibraltar Pound', N'GIP', N'GIBRALTAR' ), 
                                           ( N'959', N'Gold', N'XAU', N'ZZ08_Gold' ), 
                                           ( N'332', N'Gourde', N'HTG', N'HAITI' ), 
                                           ( N'600', N'Guarani', N'PYG', N'PARAGUAY' ), 
                                           ( N'324', N'Guinean Franc', N'GNF', N'GUINEA' ), 
                                           ( N'328', N'Guyana Dollar', N'GYD', N'GUYANA' ), 
                                           ( N'344', N'Hong Kong Dollar', N'HKD', N'HONG KONG' ), 
                                           ( N'980', N'Hryvnia', N'UAH', N'UKRAINE' ), 
                                           ( N'352', N'Iceland Krona', N'ISK', N'ICELAND' ), 
                                           ( N'356', N'Indian Rupee', N'INR', N'INDIA' ), 
                                           ( N'356', N'Indian Rupee', N'INR', N'BHUTAN' ), 
                                           ( N'364', N'Iranian Rial', N'IRR', N'IRAN (ISLAMIC REPUBLIC OF)' ), 
                                           ( N'368', N'Iraqi Dinar', N'IQD', N'IRAQ' ), 
                                           ( N'388', N'Jamaican Dollar', N'JMD', N'JAMAICA' ), 
                                           ( N'400', N'Jordanian Dinar', N'JOD', N'JORDAN' ), 
                                           ( N'404', N'Kenyan Shilling', N'KES', N'KENYA' ), 
                                           ( N'598', N'Kina', N'PGK', N'PAPUA NEW GUINEA' ), 
                                           ( N'414', N'Kuwaiti Dinar', N'KWD', N'KUWAIT' ), 
                                           ( N'973', N'Kwanza', N'AOA', N'ANGOLA' ), 
                                           ( N'104', N'Kyat', N'MMK', N'MYANMAR' ), 
                                           ( N'418', N'Lao Kip', N'LAK', N'LAO PEOPLE’S DEMOCRATIC REPUBLIC (THE)' ), 
                                           ( N'981', N'Lari', N'GEL', N'GEORGIA' ), 
                                           ( N'422', N'Lebanese Pound', N'LBP', N'LEBANON' ), 
                                           ( N'008', N'Lek', N'ALL', N'ALBANIA' ), 
                                           ( N'340', N'Lempira', N'HNL', N'HONDURAS' ), 
                                           ( N'694', N'Leone', N'SLL', N'SIERRA LEONE' ), 
                                           ( N'925', N'Leone', N'SLE', N'SIERRA LEONE' ), 
                                           ( N'430', N'Liberian Dollar', N'LRD', N'LIBERIA' ), 
                                           ( N'434', N'Libyan Dinar', N'LYD', N'LIBYA' ), 
                                           ( N'748', N'Lilangeni', N'SZL', N'ESWATINI' ), 
                                           ( N'426', N'Loti', N'LSL', N'LESOTHO' ), 
                                           ( N'969', N'Malagasy Ariary', N'MGA', N'MADAGASCAR' ), 
                                           ( N'454', N'Malawi Kwacha', N'MWK', N'MALAWI' ), 
                                           ( N'458', N'Malaysian Ringgit', N'MYR', N'MALAYSIA' ), 
                                           ( N'480', N'Mauritius Rupee', N'MUR', N'MAURITIUS' ), 
                                           ( N'484', N'Mexican Peso', N'MXN', N'MEXICO' ), 
                                           ( N'979', N'Mexican Unidad de Inversion (UDI)', N'MXV', N'MEXICO' ), 
                                           ( N'498', N'Moldovan Leu', N'MDL', N'MOLDOVA (THE REPUBLIC OF)' ), 
                                           ( N'504', N'Moroccan Dirham', N'MAD', N'MOROCCO' ), 
                                           ( N'504', N'Moroccan Dirham', N'MAD', N'WESTERN SAHARA' ), 
                                           ( N'943', N'Mozambique Metical', N'MZN', N'MOZAMBIQUE' ), 
                                           ( N'984', N'Mvdol', N'BOV', N'BOLIVIA (PLURINATIONAL STATE OF)' ), 
                                           ( N'566', N'Naira', N'NGN', N'NIGERIA' ), 
                                           ( N'232', N'Nakfa', N'ERN', N'ERITREA' ), 
                                           ( N'516', N'Namibia Dollar', N'NAD', N'NAMIBIA' ), 
                                           ( N'524', N'Nepalese Rupee', N'NPR', N'NEPAL' ), 
                                           ( N'532', N'Netherlands Antillean Guilder', N'ANG', N'SINT MAARTEN (DUTCH PART)' ), 
                                           ( N'532', N'Netherlands Antillean Guilder', N'ANG', N'CURAÇAO' ), 
                                           ( N'376', N'New Israeli Sheqel', N'ILS', N'ISRAEL' ), 
                                           ( N'901', N'New Taiwan Dollar', N'TWD', N'TAIWAN (PROVINCE OF CHINA)' ), 
                                           ( N'554', N'New Zealand Dollar', N'NZD', N'TOKELAU' ), 
                                           ( N'554', N'New Zealand Dollar', N'NZD', N'COOK ISLANDS (THE)' ), 
                                           ( N'554', N'New Zealand Dollar', N'NZD', N'NIUE' ), 
                                           ( N'554', N'New Zealand Dollar', N'NZD', N'NEW ZEALAND' ), 
                                           ( N'554', N'New Zealand Dollar', N'NZD', N'PITCAIRN' ), 
                                           ( N'064', N'Ngultrum', N'BTN', N'BHUTAN' ), 
                                           ( N'408', N'North Korean Won', N'KPW', N'KOREA (THE DEMOCRATIC PEOPLE’S REPUBLIC OF)' ), 
                                           ( N'578', N'Norwegian Krone', N'NOK', N'NORWAY' ), 
                                           ( N'578', N'Norwegian Krone', N'NOK', N'SVALBARD AND JAN MAYEN' ), 
                                           ( N'578', N'Norwegian Krone', N'NOK', N'BOUVET ISLAND' ), 
                                           ( N'929', N'Ouguiya', N'MRU', N'MAURITANIA' ), 
                                           ( N'776', N'Pa’anga', N'TOP', N'TONGA' ), 
                                           ( N'586', N'Pakistan Rupee', N'PKR', N'PAKISTAN' ), 
                                           ( N'964', N'Palladium', N'XPD', N'ZZ09_Palladium' ), 
                                           ( N'446', N'Pataca', N'MOP', N'MACAO' ), 
                                           ( N'931', N'Peso Convertible', N'CUC', N'CUBA' ), 
                                           ( N'858', N'Peso Uruguayo', N'UYU', N'URUGUAY' ), 
                                           ( N'608', N'Philippine Peso', N'PHP', N'PHILIPPINES (THE)' ), 
                                           ( N'962', N'Platinum', N'XPT', N'ZZ10_Platinum' ), 
                                           ( N'826', N'Pound Sterling', N'GBP', N'UNITED KINGDOM OF GREAT BRITAIN AND NORTHERN IRELAND (THE)' ), 
                                           ( N'826', N'Pound Sterling', N'GBP', N'JERSEY' ), 
                                           ( N'826', N'Pound Sterling', N'GBP', N'ISLE OF MAN' ), 
                                           ( N'826', N'Pound Sterling', N'GBP', N'GUERNSEY' ), 
                                           ( N'072', N'Pula', N'BWP', N'BOTSWANA' ), 
                                           ( N'634', N'Qatari Rial', N'QAR', N'QATAR' ), 
                                           ( N'320', N'Quetzal', N'GTQ', N'GUATEMALA' ), 
                                           ( N'710', N'Rand', N'ZAR', N'LESOTHO' ), 
                                           ( N'710', N'Rand', N'ZAR', N'NAMIBIA' ), 
                                           ( N'710', N'Rand', N'ZAR', N'SOUTH AFRICA' ), 
                                           ( N'512', N'Rial Omani', N'OMR', N'OMAN' ), 
                                           ( N'116', N'Riel', N'KHR', N'CAMBODIA' ), 
                                           ( N'946', N'Romanian Leu', N'RON', N'ROMANIA' ), 
                                           ( N'462', N'Rufiyaa', N'MVR', N'MALDIVES' ), 
                                           ( N'360', N'Rupiah', N'IDR', N'INDONESIA' ), 
                                           ( N'643', N'Russian Ruble', N'RUB', N'RUSSIAN FEDERATION (THE)' ), 
                                           ( N'646', N'Rwanda Franc', N'RWF', N'RWANDA' ), 
                                           ( N'654', N'Saint Helena Pound', N'SHP', N'SAINT HELENA, ASCENSION AND TRISTAN DA CUNHA' ), 
                                           ( N'682', N'Saudi Riyal', N'SAR', N'SAUDI ARABIA' ), 
                                           ( N'960', N'SDR (Special Drawing Right)', N'XDR', N'INTERNATIONAL MONETARY FUND (IMF) ' ), 
                                           ( N'941', N'Serbian Dinar', N'RSD', N'SERBIA' ), 
                                           ( N'690', N'Seychelles Rupee', N'SCR', N'SEYCHELLES' ), 
                                           ( N'961', N'Silver', N'XAG', N'ZZ11_Silver' ), 
                                           ( N'702', N'Singapore Dollar', N'SGD', N'SINGAPORE' ), 
                                           ( N'604', N'Sol', N'PEN', N'PERU' ), 
                                           ( N'090', N'Solomon Islands Dollar', N'SBD', N'SOLOMON ISLANDS' ), 
                                           ( N'417', N'Som', N'KGS', N'KYRGYZSTAN' ), 
                                           ( N'706', N'Somali Shilling', N'SOS', N'SOMALIA' ), 
                                           ( N'972', N'Somoni', N'TJS', N'TAJIKISTAN' ), 
                                           ( N'728', N'South Sudanese Pound', N'SSP', N'SOUTH SUDAN' ), 
                                           ( N'144', N'Sri Lanka Rupee', N'LKR', N'SRI LANKA' ), 
                                           ( N'994', N'Sucre', N'XSU', N'SISTEMA UNITARIO DE COMPENSACION REGIONAL DE PAGOS SUCRE' ), 
                                           (N'938', N'Sudanese Pound', N'SDG', N'SUDAN (THE)'),
                                           (N'968', N'Surinam Dollar', N'SRD', N'SURINAME'), 
                                           (N'752', N'Swedish Krona', N'SEK', N'SWEDEN' ), 
                                           (N'756', N'Swiss Franc', N'CHF', N'SWITZERLAND' ), 
                                           (N'756', N'Swiss Franc', N'CHF', N'LIECHTENSTEIN' ), 
                                           (N'760', N'Syrian Pound', N'SYP', N'SYRIAN ARAB REPUBLIC' ), 
                                           (N'050', N'Taka', N'BDT', N'BANGLADESH' ), 
                                           (N'882', N'Tala', N'WST', N'SAMOA' ), 
                                           (N'834', N'Tanzanian Shilling', N'TZS', N'TANZANIA, UNITED REPUBLIC OF' ), 
                                           (N'398', N'Tenge', N'KZT', N'KAZAKHSTAN' ), 
                                           (N'999', N'The codes assigned for transactions where no currency is involved', N'XXX', N'ZZ07_No_Currency' ), 
                                           (N'780', N'Trinidad and Tobago Dollar', N'TTD', N'TRINIDAD AND TOBAGO' ), 
                                           (N'496', N'Tugrik', N'MNT', N'MONGOLIA' ), 
                                           (N'788', N'Tunisian Dinar', N'TND', N'TUNISIA' ), 
                                           (N'949', N'Turkish Lira', N'TRY', N'TÜRKİYE' ), 
                                           (N'934', N'Turkmenistan New Manat', N'TMT', N'TURKMENISTAN' ), 
                                           (N'784', N'UAE Dirham', N'AED', N'UNITED ARAB EMIRATES (THE)' ), 
                                           (N'800', N'Uganda Shilling', N'UGX', N'UGANDA' ), 
                                           (N'990', N'Unidad de Fomento', N'CLF', N'CHILE' ), 
                                           (N'970', N'Unidad de Valor Real', N'COU', N'COLOMBIA' ), 
                                           (N'927', N'Unidad Previsional', N'UYW', N'URUGUAY' ), 
                                           (N'940', N'Uruguay Peso en Unidades Indexadas (UI)', N'UYI', N'URUGUAY' ), 
                                           (N'840', N'US Dollar', N'USD', N'VIRGIN ISLANDS (BRITISH)' ), 
                                           (N'840', N'US Dollar', N'USD', N'VIRGIN ISLANDS (U.S.)' ), 
                                           (N'840', N'US Dollar', N'USD', N'UNITED STATES MINOR OUTLYING ISLANDS (THE)' ), 
                                           (N'840', N'US Dollar', N'USD', N'UNITED STATES OF AMERICA (THE)' ), 
                                           (N'840', N'US Dollar', N'USD', N'TURKS AND CAICOS ISLANDS (THE)' ), 
                                           (N'840', N'US Dollar', N'USD', N'TIMOR-LESTE' ), 
                                           (N'840', N'US Dollar', N'USD', N'AMERICAN SAMOA' ), 
                                           (N'840', N'US Dollar', N'USD', N'BRITISH INDIAN OCEAN TERRITORY (THE)' ), 
                                           (N'840', N'US Dollar', N'USD', N'BONAIRE, SINT EUSTATIUS AND SABA' ), 
                                           (N'840', N'US Dollar', N'USD', N'EL SALVADOR' ), 
                                           (N'840', N'US Dollar', N'USD', N'ECUADOR' ), 
                                           (N'840', N'US Dollar', N'USD', N'NORTHERN MARIANA ISLANDS (THE)' ), 
                                           (N'840', N'US Dollar', N'USD', N'PUERTO RICO' ), 
                                           (N'840', N'US Dollar', N'USD', N'PALAU' ), 
                                           (N'840', N'US Dollar', N'USD', N'PANAMA' ), 
                                           (N'840', N'US Dollar', N'USD', N'MARSHALL ISLANDS (THE)' ), 
                                           (N'840', N'US Dollar', N'USD', N'MICRONESIA (FEDERATED STATES OF)' ), 
                                           (N'840', N'US Dollar', N'USD', N'HAITI' ), 
                                           (N'840', N'US Dollar', N'USD', N'GUAM' ), 
                                           (N'997', N'US Dollar (Next day)', N'USN', N'UNITED STATES OF AMERICA (THE)' ), 
                                           (N'860', N'Uzbekistan Sum', N'UZS', N'UZBEKISTAN' ), 
                                           (N'548', N'Vatu', N'VUV', N'VANUATU' ), 
                                           (N'947', N'WIR Euro', N'CHE', N'SWITZERLAND' ), 
                                           (N'948', N'WIR Franc', N'CHW', N'SWITZERLAND' ), 
                                           (N'410', N'Won', N'KRW', N'KOREA (THE REPUBLIC OF)' ), 
                                           (N'886', N'Yemeni Rial', N'YER', N'YEMEN' ), 
                                           (N'392', N'Yen', N'JPY', N'JAPAN' ), 
                                           (N'156', N'Yuan Renminbi', N'CNY', N'CHINA' ), 
                                           (N'967', N'Zambian Kwacha', N'ZMW', N'ZAMBIA' ), 
                                           (N'932', N'Zimbabwe Dollar', N'ZWL', N'ZIMBABWE' ), 
                                           (N'985', N'Zloty', N'PLN', N'POLAND' )
                                           "); } catch { }

            #endregion

            #region CREATE > INSERT TCOD_VAHED_EXTENDED
            try
            {
                dbms.DoExecuteSQL(@"CREATE TABLE [dbo].[TCOD_VAHED_EXTENDED](
                                        	[IDD] [int] NOT NULL,
                                        	[NAME_MO] [nvarchar](50) NOT NULL,
                                        	[CRT] [datetime] NULL,
                                        	[UID] [int] NULL,
                                         CONSTRAINT [PK_TCOD_VAHED_EXTENDEDD] PRIMARY KEY CLUSTERED 
                                         (
                                         	[IDD] ASC
                                         )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
                                         ) ON [PRIMARY] ");
            }
            catch { }

            try { dbms.DoExecuteSQL(@"ALTER TABLE [dbo].[TCOD_VAHED_EXTENDED] ADD  DEFAULT (getdate()) FOR [CRT]"); } catch { }

            try
            {
                dbms.DoExecuteSQL(@"INSERT INTO TCOD_VAHED_EXTENDED ([IDD], [NAME_MO])
                                    VALUES
                                    ( 161, N'برگ' ), 
                                    ( 162, N'تیوب' ), 
                                    ( 163, N'قالب' ), 
                                    ( 164, N'کیلوگرم' ), 
                                    ( 165, N'متر' ), 
                                    ( 166, N'صفحه' ), 
                                    ( 169, N'تن' ), 
                                    ( 1610, N'کلاف' ), 
                                    ( 1611, N'لنگه' ), 
                                    ( 1612, N'عدل' ), 
                                    ( 1613, N'جعبه' ), 
                                    ( 1614, N'گالن' ), 
                                    ( 1615, N'کیسه' ), 
                                    ( 1617, N'جلد' ), 
                                    ( 1618, N'توپ' ), 
                                    ( 1619, N'ست' ), 
                                    ( 1620, N'دست' ), 
                                    ( 1624, N'کارتن' ), 
                                    ( 1625, N'سطل' ), 
                                    ( 1626, N'تانكر' ), 
                                    ( 1627, N'عدد' ), 
                                    ( 1628, N'بسته' ), 
                                    ( 1629, N'پاکت' ), 
                                    ( 1630, N'(رول) حلقه' ), 
                                    ( 1631, N'دستگاه' ), 
                                    ( 1633, N'سیلندر' ), 
                                    ( 1635, N'قرقره' ), 
                                    ( 1637, N'لیتر' ), 
                                    ( 1638, N'بطری' ), 
                                    ( 1639, N'بشكه' ), 
                                    ( 1640, N'تخته' ), 
                                    ( 1641, N'رول' ), 
                                    ( 1642, N'طاقه' ), 
                                    ( 1643, N'جفت' ), 
                                    ( 1644, N'قوطي' ), 
                                    ( 1645, N'متر مربع' ), 
                                    ( 1646, N'شاخه' ), 
                                    ( 1647, N'متر مكعب' ), 
                                    ( 1648, N'دبه' ), 
                                    ( 1649, N'پالت' ), 
                                    ( 1650, N'ساشه' ), 
                                    ( 1651, N'بانكه' ), 
                                    ( 1654, N'ورق' ), 
                                    ( 1656, N'بندیل' ), 
                                    ( 1660, N'شانه' ), 
                                    ( 1661, N'دوجین' ), 
                                    ( 1666, N'مخزن' ), 
                                    ( 1668, N'(رینگ) حلقه' ), 
                                    ( 1673, N'قراص' ), 
                                    ( 1679, N'فوت مربع' ), 
                                    ( 1680, N'طغرا' ), 
                                    ( 1683, N'کپسول' ), 
                                    ( 1684, N'سبد' ), 
                                    ( 1687, N'فاقد بسته بندی' ), 
                                    ( 1689, N'ثوب' ), 
                                    ( 1690, N'نیم دوجین' ), 
                                    ( 1693, N'(master case) کارتن' ), 
                                    ( 1694, N'(bundle) قراصه' )");
            }
            catch { }

            try
            {
                dbms.DoExecuteSQL(@"
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'برگ' WHERE IDD = 161;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'تیوب' WHERE IDD = 162;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'قالب' WHERE IDD = 163;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'کیلوگرم' WHERE IDD = 164;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'متر' WHERE IDD = 165;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'صفحه' WHERE IDD = 166;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'تن' WHERE IDD = 169;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'کلاف' WHERE IDD = 1610;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'لنگه' WHERE IDD = 1611;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'عدل' WHERE IDD = 1612;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'جعبه' WHERE IDD = 1613;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'گالن' WHERE IDD = 1614;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'کیسه' WHERE IDD = 1615;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'جلد' WHERE IDD = 1617;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'توپ' WHERE IDD = 1618;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'ست' WHERE IDD = 1619;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'دست' WHERE IDD = 1620;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'کارتن' WHERE IDD = 1624;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'سطل' WHERE IDD = 1625;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'تانكر' WHERE IDD = 1626;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'عدد' WHERE IDD = 1627;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'بسته' WHERE IDD = 1628;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'پاکت' WHERE IDD = 1629;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'(رول) حلقه' WHERE IDD = 1630;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'دستگاه' WHERE IDD = 1631;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'سیلندر' WHERE IDD = 1633;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'قرقره' WHERE IDD = 1635;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'لیتر' WHERE IDD = 1637;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'بطری' WHERE IDD = 1638;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'بشكه' WHERE IDD = 1639;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'تخته' WHERE IDD = 1640;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'رول' WHERE IDD = 1641;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'طاقه' WHERE IDD = 1642;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'جفت' WHERE IDD = 1643;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'قوطي' WHERE IDD = 1644;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'متر مربع' WHERE IDD = 1645;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'شاخه' WHERE IDD = 1646;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'متر مكعب' WHERE IDD = 1647;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'دبه' WHERE IDD = 1648;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'پالت' WHERE IDD = 1649;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'ساشه' WHERE IDD = 1650;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'بانكه' WHERE IDD = 1651;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'ورق' WHERE IDD = 1654;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'بندیل' WHERE IDD = 1656;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'شانه' WHERE IDD = 1660;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'دوجین' WHERE IDD = 1661;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'مخزن' WHERE IDD = 1666;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'(رینگ) حلقه' WHERE IDD = 1668;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'قراص' WHERE IDD = 1673;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'فوت مربع' WHERE IDD = 1679;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'طغرا' WHERE IDD = 1680;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'کپسول' WHERE IDD = 1683;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'سبد' WHERE IDD = 1684;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'فاقد بسته بندی' WHERE IDD = 1687;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'ثوب' WHERE IDD = 1689;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'نیم دوجین' WHERE IDD = 1690;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'(master case) کارتن' WHERE IDD = 1693;
                                UPDATE TCOD_VAHED_EXTENDED SET NAME_MO = N'(bundle) قراصه' WHERE IDD = 1694 ");
            }
            catch { }
            #endregion


            //CHANGE : SAZMAN
            try { dbms.DoExecuteSQL(@"ALTER TABLE SAZMAN ADD Dcertificate nvarchar(4000) DEFAULT 'Checked'"); } catch { }
            try { dbms.DoExecuteSQL(@"ALTER TABLE SAZMAN ADD MEMORYIDsand nvarchar(6) DEFAULT 'Checked'"); } catch { }

            //CHANGE : HEAD_LST & INVO_LST _EXTENDED
            try { dbms.DoExecuteSQL(@"ALTER TABLE [dbo].[INVO_LST_EXTENDED] ADD  DEFAULT (getdate()) FOR [CRT]"); } catch { }

            try { dbms.DoExecuteSQL("ALTER TABLE HEAD_LST_EXTENDED ADD exr float NULL"); } catch { }
            try { dbms.DoExecuteSQL("ALTER TABLE HEAD_LST_EXTENDED ADD sscv float NULL"); } catch { }
            try { dbms.DoExecuteSQL("ALTER TABLE [dbo].[HEAD_LST_EXTENDED] ADD [CUT] nvarchar(3) NULL"); } catch { }
            try { dbms.DoExecuteSQL("ALTER TABLE [dbo].[HEAD_LST_EXTENDED] ADD [irtaxid] nvarchar(22) NULL"); } catch { }

            //CHANGE STUF_DEF
            try { dbms.DoExecuteSQL(@"ALTER TABLE dbo.STUF_DEF ADD sstid nvarchar(13) NULL ; ALTER TABLE dbo.STUF_DEF ADD vra float NULL"); } catch { }

            try { dbms.DoExecuteSQL(@"ALTER TABLE dbo.TAXDTL ALTER COLUMN UID NVARCHAR(100)"); } catch { }

            try { dbms.DoExecuteSQL(@"ALTER TABLE dbo.TAXDTL ADD Indatim_Sec bigint"); } catch { }
            try { dbms.DoExecuteSQL(@"ALTER TABLE dbo.TAXDTL ADD Indati2m_Sec bigint"); } catch { }

            try { dbms.DoExecuteSQL(@"ALTER TABLE dbo.TAXDTL
                                      ADD NUMBER FLOAT NULL,
                                          TAG FLOAT NULL,
                                          DATE_N bigint NULL "); } catch { }


            //ادامه شماره فاکتور از سال قبل
            try { dbms.DoExecuteSQL(@"ALTER TABLE [dbo].[SAZMAN] ADD [MOADINA_SCNUM] [decimal] (18,0) NOT NULL CONSTRAINT [DF_SAZMAN_MOADINA_SCNUM] DEFAULT (1)"); } catch { }

            try { dbms.DoExecuteSQL(@"ALTER TABLE dbo.TAXDTL ADD REMARKS NVARCHAR(4000) NULL"); } catch { }

            try { File.Delete("C:\\CORRECT\\DBMSLOG.txt"); } catch { }
        }
    }
}
