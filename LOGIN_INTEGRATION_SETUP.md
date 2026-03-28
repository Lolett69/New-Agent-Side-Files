# Login Integration Setup Guide

This document outlines the steps to integrate the login functionality from the NextHorizon repository into your Agent Dashboard.

## Files Imported

### Controllers
- **LoginController.cs** - Handles login, logout, and authentication logic

### Models
- **LoginViewModel.cs** - ViewModel for login form
- **UserModel.cs** - User entity model
- **AppDbContext.cs** - Updated Entity Framework DbContext with Users, StaffInfo, and AuditLog tables

### Services
- **IAuthService.cs** - Authentication service interface
- **AuthService.cs** - Authentication service implementation

### Views
- **Pages/Login/AdminLogin.cshtml** - Login page UI

### Frontend Assets
- **wwwroot/css/adminLogin.css** - Login page styles
- **wwwroot/js/adminLogin.js** - Login page scripts

## Setup Steps

### 1. Database Setup

Before using the login functionality, you need to create the required database tables. The AppDbContext includes configurations for:

- `users` table - Core user information
- `staff_info` table - Staff-specific information
- `audit_logs` table - Activity logging

**Option A: Using Entity Framework Migrations**
```powershell
# In Package Manager Console
Add-Migration AddLoginTables
Update-Database
```

**Option B: Using SQL Script**
Execute the SQL script provided in your database to create these tables.

### 2. Configuration Updates

Update your `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=YOUR_DATABASE;User Id=YOUR_USER;Password=YOUR_PASSWORD;"
  },
  "Jwt": {
    "SecretKey": "your-secret-key-here",
    "Issuer": "your-issuer",
    "Audience": "your-audience"
  }
}
```

### 3. Dependency Injection Setup

In `Program.cs`, register the authentication services:

```csharp
// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Authentication Services
builder.Services.AddScoped<IAuthService, AuthService>();

// Add Session support
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add Authentication
builder.Services.AddAuthentication("CookieAuth")
    .AddCookie("CookieAuth", options =>
    {
        options.LoginPath = "/Login/AdminLogin";
        options.LogoutPath = "/Login/Logout";
        options.AccessDeniedPath = "/Login/AccessDenied";
    });

// Middleware
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
```

### 4. Routing Configuration

Ensure your route configuration in `Program.cs` includes:

```csharp
app.MapControllers();
app.MapRazorPages();
```

### 5. Link Login Page to Your Application

Update your layout files to include a link to the login page:

```html
<a href="/login/adminlogin">Login</a>
```

### 6. Testing

1. Run your application
2. Navigate to `/Login/AdminLogin`
3. Test with a valid username/password combination from your database
4. Verify that you can log in and out

## Important Notes

### User Creation
You need to create users in the database manually or through an admin interface. Example SQL:

```sql
INSERT INTO users (username, email, password_hash, user_type, status)
VALUES ('admin', 'admin@example.com', 'hashed_password_here', 'admin', 'active')
```

### Password Hashing
The system uses `PasswordHasher<object>` from `Microsoft.AspNetCore.Identity`. 

To hash a password for testing:
```csharp
var hasher = new PasswordHasher<object>();
string hashedPassword = hasher.HashPassword(null, "YourPlainPassword");
```

### Security Considerations
- Always use HTTPS in production
- Implement rate limiting on login attempts
- Store sensitive configuration in user secrets or environment variables
- Consider implementing 2FA for additional security
- Regularly audit the audit_logs table for suspicious activities

## Troubleshooting

### Connection String Issues
- Verify the connection string in `appsettings.json`
- Ensure the database server is running
- Check firewall rules

### Authentication Failures
- Check user exists in database
- Verify password is correctly hashed
- Check session configuration
- Review browser console for JavaScript errors

### Missing Tables
- Run Entity Framework migrations
- Or execute the SQL scripts provided

## Next Steps

1. ✅ Review and test the login functionality
2. ✅ Integrate login checks with your Agent Dashboard
3. ✅ Add role-based authorization to protected pages
4. ✅ Customize the login UI to match your branding
5. ✅ Implement additional security measures as needed

## Support

If you encounter issues, check:
- Visual Studio Output window for build errors
- Browser Developer Tools console for frontend errors
- SQL Server logs for database errors
- Application logs for runtime exceptions
