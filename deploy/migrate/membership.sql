-- Step 3 of docs/runbook.md section 10: the application's login joins the role that migration 0020_DatabaseRoles made.
-- Run as the administrator, after the migrations; does nothing the second time.
IF IS_ROLEMEMBER(N'billingrecon_application', N'billingrecon_app') = 0
    ALTER ROLE [billingrecon_application] ADD MEMBER [billingrecon_app];
GO
