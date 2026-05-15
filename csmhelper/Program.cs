using csmhelper.services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// ����������� ��������
builder.Services.AddScoped<IJiraService, JiraService>();
builder.Services.AddScoped<IGantService, GantService>();
builder.Services.AddScoped<IGanttService, GanttService>();

// ��������� ������
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpClient<IJiraService, JiraService>(client =>
{
    client.BaseAddress = new Uri("https://jira.moscow.alfaintra.net");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        // ��� ������������� ������������ ����� �������� �������������� ������
        if (errors == System.Net.Security.SslPolicyErrors.None)
            return true;

        // ��� �������� ������������ ������������
        if (cert?.Issuer.Contains("alfaintra") == true)
            return true;

        return errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors;
    }
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.UseSession(); // �������� ��������� ������

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
