-- FNRC Service Desk: create the database. First installation only.
--
-- Run in SQL Server Management Studio as a SQL Server administrator.
-- To use another name, change it on both lines below and in the app's connection string.
-- The files go to the server's default data and log folders; the DBA can choose others.
-- Add the database to the server's backup plan, together with the attachment and evidence shares.

USE [master];
GO

IF DB_ID(N'FnrcItsm') IS NULL
    CREATE DATABASE [FnrcItsm];
GO
