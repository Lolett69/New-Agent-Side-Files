-- Rollback Migration: Remove Inactivity Tracking Columns from SupportFAQs
-- Purpose: Remove LastActivityTime and InactivityWarningShown columns

BEGIN TRANSACTION;

BEGIN TRY
    -- Drop LastActivityTime column
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
        WHERE TABLE_NAME = 'SupportFAQs' AND COLUMN_NAME = 'LastActivityTime'
    )
    BEGIN
        ALTER TABLE dbo.SupportFAQs
        DROP COLUMN LastActivityTime;
        PRINT 'Removed LastActivityTime column from SupportFAQs table.';
    END

    -- Find and drop the default constraint for InactivityWarningShown
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
        WHERE TABLE_NAME = 'SupportFAQs' AND COLUMN_NAME = 'InactivityWarningShown'
    )
    BEGIN
        DECLARE @ConstraintName NVARCHAR(128);
        SELECT @ConstraintName = name
        FROM sys.default_constraints
        WHERE parent_object_id = OBJECT_ID('dbo.SupportFAQs')
        AND parent_column_id = (SELECT column_id FROM sys.columns 
                                WHERE object_id = OBJECT_ID('dbo.SupportFAQs') 
                                AND name = 'InactivityWarningShown');

        IF @ConstraintName IS NOT NULL
        BEGIN
            DECLARE @DropConstraint NVARCHAR(256) = 'ALTER TABLE dbo.SupportFAQs DROP CONSTRAINT ' + @ConstraintName;
            EXEC sp_executesql @DropConstraint;
            PRINT 'Dropped default constraint: ' + @ConstraintName;
        END

        -- Now drop the column
        ALTER TABLE dbo.SupportFAQs
        DROP COLUMN InactivityWarningShown;
        PRINT 'Removed InactivityWarningShown column from SupportFAQs table.';
    END

    COMMIT TRANSACTION;
    PRINT 'Rollback migration completed successfully.';
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    PRINT 'Rollback migration failed: ' + @ErrorMessage;
    THROW;
END CATCH
