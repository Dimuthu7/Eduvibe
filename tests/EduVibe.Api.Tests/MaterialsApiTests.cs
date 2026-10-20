using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EduVibe.Api.Tests;

/// <summary>Class materials kept in the teacher's Google Drive: connecting, adding and opening files, who may see them, and what the privacy export holds.</summary>
public class MaterialsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 7000;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private async Task GoPro(Guid teacherId)
    {
        var admin = await factory.AdminClientAsync();
        var plans = await Json(await admin.GetAsync("/api/plans/plans"));
        var pro = plans.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Pro").GetProperty("id").GetGuid();
        var assigned = await admin.PostAsJsonAsync($"/api/plans/teachers/{teacherId}/subscription", new { planId = pro, note = "test" });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
    }

    private async Task<TestTeacher> ProTeacher(string name = "Materia")
    {
        var teacher = await factory.NewTeacherAsync(name);
        await GoPro(teacher.Id);
        return teacher;
    }

    /// <summary>Walks the teacher through Google's consent: asks for the address, then plays Google's redirect back with a code.</summary>
    private async Task<HttpResponseMessage> Connect(HttpClient teacher, string code = "ok")
    {
        var consent = await Json(await teacher.PostAsync("/api/materials/drive/connect", null));
        var state = Uri.UnescapeDataString(consent.GetProperty("url").GetString()!.Split("state=")[1]);
        var browser = factory.CreateClient(new() { AllowAutoRedirect = false });
        return await browser.GetAsync($"/api/materials/drive/callback?code={code}&state={Uri.EscapeDataString(state)}");
    }

    private async Task<Guid> ClassOf(HttpClient teacher, string title = "Materials class")
    {
        var venueId = (await Json(await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Hall " + title, district = "Kandy" }))).GetProperty("id").GetGuid();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = 2000m, slots = new[] { new { day = 2, start = "16:00", end = "18:00" } },
        });
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent Upload(Guid classId, string fileName, byte[]? bytes = null, string? title = null, string kind = "PastPaper", string? topic = null, string? year = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(classId.ToString()), "classId" },
            { new StringContent(kind), "kind" },
        };
        if (title is not null) { form.Add(new StringContent(title), "title"); }
        if (topic is not null) { form.Add(new StringContent(topic), "topic"); }
        if (year is not null) { form.Add(new StringContent(year), "year"); }
        var file = new ByteArrayContent(bytes ?? Encoding.UTF8.GetBytes("%PDF-1.4 hello"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return form;
    }

    private async Task<JsonElement> AddMaterial(HttpClient teacher, Guid classId, string fileName = "paper.pdf", string? title = "Paper", string kind = "PastPaper", string? topic = "Algebra", string? year = "2024")
    {
        var response = await teacher.PostAsync("/api/materials", Upload(classId, fileName, title: title, kind: kind, topic: topic, year: year));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private sealed record Enrolled(Guid StudentId, Guid EnrolmentId, string Phone, string Otp);

    private static async Task<Enrolled> Enrol(HttpClient teacher, Guid classId, string first)
    {
        var phone = $"076{Interlocked.Increment(ref _phoneCounter):D7}";
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone, firstName = first, lastName = "Reader", classId, feePlan = "Full" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        var row = body.GetProperty("row");
        return new Enrolled(row.GetProperty("studentId").GetGuid(), row.GetProperty("enrolmentId").GetGuid(), phone, body.GetProperty("oneTimePassword").GetString()!);
    }

    private async Task<HttpClient> SignInStudent(Enrolled student)
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = student.Phone, password = student.Otp });
        var setup = factory.CreateClient();
        setup.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString()!);
        var changed = await setup.PostAsJsonAsync("/api/identity/change-password",
            new { currentPassword = student.Otp, newPassword = "Student-pass-1", username = "m" + Guid.NewGuid().ToString("N")[..10] });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(changed)).GetProperty("accessToken").GetString()!);
        return client;
    }

    [Fact]
    public async Task Free_teachers_are_told_the_feature_is_on_Pro_and_Pro_teachers_can_use_it()
    {
        var teacher = await factory.NewTeacherAsync("Freeman");
        Assert.Equal("plan_feature_locked", await Code(await teacher.Client.GetAsync("/api/materials/drive")));
        Assert.Equal("plan_feature_locked", await Code(await teacher.Client.GetAsync("/api/materials")));

        await GoPro(teacher.Id);
        var status = await Json(await teacher.Client.GetAsync("/api/materials/drive"));
        Assert.True(status.GetProperty("configured").GetBoolean());
        Assert.False(status.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Connecting_Drive_needs_a_configured_Google_client_and_remembers_which_account()
    {
        var teacher = await ProTeacher("Linker");
        factory.Drive.IsConfigured = false;
        try
        {
            Assert.Equal("drive_not_configured", await Code(await teacher.Client.PostAsync("/api/materials/drive/connect", null)));
        }
        finally
        {
            factory.Drive.IsConfigured = true;
        }

        var back = await Connect(teacher.Client);
        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        Assert.EndsWith("/materials?drive=connected", back.Headers.Location!.ToString());

        var status = await Json(await teacher.Client.GetAsync("/api/materials/drive"));
        Assert.True(status.GetProperty("connected").GetBoolean());
        Assert.Equal("teacher@example.com", status.GetProperty("email").GetString());
        Assert.DoesNotContain("refresh", status.ToString());
    }

    [Fact]
    public async Task The_callback_refuses_a_made_up_state_and_reports_a_denied_or_refused_consent()
    {
        var teacher = await ProTeacher("Careful");
        var browser = factory.CreateClient(new() { AllowAutoRedirect = false });

        var forged = await browser.GetAsync("/api/materials/drive/callback?code=ok&state=not-a-real-state");
        Assert.EndsWith("drive=failed", forged.Headers.Location!.ToString());

        var denied = await Connect(teacher.Client, code: "");
        Assert.EndsWith("drive=denied", denied.Headers.Location!.ToString());

        var refused = await Connect(teacher.Client, code: "refused");
        Assert.EndsWith("drive=permission", refused.Headers.Location!.ToString());
        Assert.False((await Json(await teacher.Client.GetAsync("/api/materials/drive"))).GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Adding_a_file_puts_it_on_its_shelf_in_the_class_folder_inside_the_EduVibe_folder_and_lists_it()
    {
        var teacher = await ProTeacher("Uploader");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client, "Grade 11 Maths");

        var added = await AddMaterial(teacher.Client, classId, "2024 paper.pdf", "2024 Past paper", "PastPaper", "Algebra", "2024");
        Assert.Equal("PastPaper", added.GetProperty("kind").GetString());
        Assert.True(added.GetProperty("canPreview").GetBoolean());
        Assert.DoesNotContain("driveFileId", added.ToString());

        var stored = factory.Drive.Files.Values.Single(f => f.Name == "2024 paper.pdf");
        Assert.Equal("application/pdf", stored.ContentType);
        var shelf = factory.Drive.Folders[stored.Folder];
        Assert.Equal("Past papers", shelf.Name);
        var classFolder = factory.Drive.Folders[shelf.Parent!];
        Assert.Equal("Grade 11 Maths", classFolder.Name);
        Assert.Equal("EduVibe", factory.Drive.Folders[classFolder.Parent!].Name);

        // A second file of the same kind reuses the folders; the first file of another kind only adds its own shelf.
        var folders = factory.Drive.Folders.Count;
        await AddMaterial(teacher.Client, classId, "paper 2.pdf", "Paper 2", "PastPaper", "Algebra", "2023");
        Assert.Equal(folders, factory.Drive.Folders.Count);
        await AddMaterial(teacher.Client, classId, "notes.docx", "Notes", "Notes", "Algebra", null);
        Assert.Equal(folders + 1, factory.Drive.Folders.Count);
        Assert.Equal(classFolder.Name, factory.Drive.Folders[factory.Drive.Folders[factory.Drive.Files.Values.Single(f => f.Name == "notes.docx").Folder].Parent!].Name);

        var all = await Json(await teacher.Client.GetAsync($"/api/materials?classId={classId}"));
        Assert.Equal(3, all.GetArrayLength());
        Assert.Equal(1, (await Json(await teacher.Client.GetAsync($"/api/materials?classId={classId}&kind=Notes"))).GetArrayLength());
        Assert.Equal(1, (await Json(await teacher.Client.GetAsync($"/api/materials?classId={classId}&year=2024"))).GetArrayLength());
        Assert.Equal(3, (await Json(await teacher.Client.GetAsync($"/api/materials?classId={classId}&topic=Algebra"))).GetArrayLength());
        Assert.Equal(0, (await Json(await teacher.Client.GetAsync($"/api/materials?classId={classId}&topic=Geometry"))).GetArrayLength());
    }

    private async Task<JsonElement> NewFolder(HttpClient teacher, Guid classId, string name, string kind = "Notes", Guid? parentId = null)
    {
        var response = await teacher.PostAsJsonAsync("/api/materials/folders", new { classId, kind, parentId, name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private static string DrivePath(FakeDriveClient drive, string folderId)
    {
        var names = new List<string>();
        for (string? id = folderId; id is not null && drive.Folders.TryGetValue(id, out var folder); id = folder.Parent)
        {
            names.Insert(0, folder.Name);
        }

        return string.Join('/', names);
    }

    [Fact]
    public async Task Folders_nest_inside_a_shelf_and_are_real_folders_in_Drive_that_files_can_be_added_to()
    {
        var teacher = await ProTeacher("Folders");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client, "Grade 10");

        var term = await NewFolder(teacher.Client, classId, "Term 1");
        Assert.Equal(JsonValueKind.Null, term.GetProperty("parentId").ValueKind);
        var week = await NewFolder(teacher.Client, classId, "Week 1", parentId: term.GetProperty("id").GetGuid());
        Assert.Equal(term.GetProperty("id").GetGuid(), week.GetProperty("parentId").GetGuid());
        await NewFolder(teacher.Client, classId, "2023", "PastPaper");

        var listed = await Json(await teacher.Client.GetAsync($"/api/materials/folders?classId={classId}"));
        Assert.Equal(["2023", "Term 1", "Week 1"], listed.EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToArray());
        Assert.DoesNotContain(listed.EnumerateArray(), f => f.GetProperty("name").GetString() == "Notes");

        var drivePaths = factory.Drive.Folders.Keys.Select(id => DrivePath(factory.Drive, id)).ToList();
        Assert.Contains("EduVibe/Grade 10/Notes/Term 1/Week 1", drivePaths);
        Assert.Contains("EduVibe/Grade 10/Past papers/2023", drivePaths);

        var form = Upload(classId, "week1.pdf", title: "Week 1 notes", kind: "Notes");
        form.Add(new StringContent(week.GetProperty("id").GetString()!), "folderId");
        var added = await Json(await teacher.Client.PostAsync("/api/materials", form));
        Assert.Equal(week.GetProperty("id").GetGuid(), added.GetProperty("folderId").GetGuid());
        Assert.Equal("EduVibe/Grade 10/Notes/Term 1/Week 1", DrivePath(factory.Drive, factory.Drive.Files.Values.Single(f => f.Name == "week1.pdf").Folder));
    }

    [Fact]
    public async Task Folder_names_are_unique_beside_each_other_and_depth_and_kind_are_checked()
    {
        var teacher = await ProTeacher("Strict");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client);
        var top = await NewFolder(teacher.Client, classId, "Term 1");
        var topId = top.GetProperty("id").GetGuid();

        var again = await teacher.Client.PostAsJsonAsync("/api/materials/folders", new { classId, kind = "Notes", name = " term 1 " });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("folder_name_taken", await Code(again));
        await NewFolder(teacher.Client, classId, "Term 1", "Other");
        await NewFolder(teacher.Client, classId, "Term 1", parentId: topId);

        Assert.Equal("folder_invalid", await Code(await teacher.Client.PostAsJsonAsync("/api/materials/folders", new { classId, kind = "Notes", name = "  " })));
        Assert.Equal("folder_invalid", await Code(await teacher.Client.PostAsJsonAsync("/api/materials/folders", new { classId, kind = "Notes", name = new string('a', 61) })));
        Assert.Equal("folder_invalid", await Code(await teacher.Client.PostAsJsonAsync("/api/materials/folders", new { classId, kind = "Other", parentId = topId, name = "Wrong shelf" })));
        Assert.Equal("material_class_required", await Code(await teacher.Client.PostAsJsonAsync("/api/materials/folders", new { classId = Guid.NewGuid(), kind = "Notes", name = "x" })));

        var parent = topId;
        for (var level = 2; level <= 4; level++)
        {
            parent = (await NewFolder(teacher.Client, classId, "Level " + level, parentId: parent)).GetProperty("id").GetGuid();
        }

        Assert.Equal("folder_too_deep", await Code(await teacher.Client.PostAsJsonAsync("/api/materials/folders", new { classId, kind = "Notes", parentId = parent, name = "Level 5" })));

        // A file may only go in a folder of its own class and shelf.
        var form = Upload(classId, "x.pdf", kind: "PastPaper");
        form.Add(new StringContent(topId.ToString()), "folderId");
        Assert.Equal("material_folder_invalid", await Code(await teacher.Client.PostAsync("/api/materials", form)));
    }

    [Fact]
    public async Task Renaming_a_folder_renames_it_in_Drive_and_only_an_empty_folder_can_be_removed()
    {
        var teacher = await ProTeacher("Renamer");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client, "Grade 9");
        var term = await NewFolder(teacher.Client, classId, "Term 1");
        var termId = term.GetProperty("id").GetGuid();
        var week = await NewFolder(teacher.Client, classId, "Renamer week", parentId: termId);
        var form = Upload(classId, "w.pdf", kind: "Notes");
        form.Add(new StringContent(week.GetProperty("id").GetString()!), "folderId");
        var file = (await Json(await teacher.Client.PostAsync("/api/materials", form))).GetProperty("id").GetGuid();

        var renamed = await teacher.Client.PutAsJsonAsync($"/api/materials/folders/{termId}", new { name = "First term" });
        Assert.Equal("First term", (await Json(renamed)).GetProperty("name").GetString());
        Assert.Contains("EduVibe/Grade 9/Notes/First term/Renamer week", factory.Drive.Folders.Keys.Select(id => DrivePath(factory.Drive, id)));
        await NewFolder(teacher.Client, classId, "Term 2");
        Assert.Equal("folder_name_taken", await Code(await teacher.Client.PutAsJsonAsync($"/api/materials/folders/{termId}", new { name = "term 2" })));
        Assert.Equal("folder_invalid", await Code(await teacher.Client.PutAsJsonAsync($"/api/materials/folders/{termId}", new { name = "" })));

        Assert.Equal("folder_not_empty", await Code(await teacher.Client.DeleteAsync($"/api/materials/folders/{termId}")));
        Assert.Equal("folder_not_empty", await Code(await teacher.Client.DeleteAsync($"/api/materials/folders/{week.GetProperty("id").GetGuid()}")));
        await teacher.Client.DeleteAsync($"/api/materials/{file}");
        var weekDrive = factory.Drive.Folders.Single(f => f.Value.Name == "Renamer week").Key;
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync($"/api/materials/folders/{week.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.False(factory.Drive.Folders.ContainsKey(weekDrive));
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync($"/api/materials/folders/{termId}")).StatusCode);
        Assert.Equal(1, (await Json(await teacher.Client.GetAsync($"/api/materials/folders?classId={classId}"))).GetArrayLength());
    }

    [Fact]
    public async Task Moving_a_material_to_another_folder_or_shelf_moves_the_file_in_Drive()
    {
        var teacher = await ProTeacher("Mover");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client, "Grade 8");
        var term = (await NewFolder(teacher.Client, classId, "Term 1")).GetProperty("id").GetGuid();
        var id = (await AddMaterial(teacher.Client, classId, "m.pdf", "Moving", "Notes")).GetProperty("id").GetGuid();
        string Where() => DrivePath(factory.Drive, factory.Drive.Files.Values.Single(f => f.Name == "m.pdf").Folder);
        Assert.Equal("EduVibe/Grade 8/Notes", Where());

        var moved = await teacher.Client.PutAsJsonAsync($"/api/materials/{id}", new { title = "Moving", kind = "Notes", folderId = term });
        Assert.Equal(term, (await Json(moved)).GetProperty("folderId").GetGuid());
        Assert.Equal("EduVibe/Grade 8/Notes/Term 1", Where());

        // Changing the shelf takes the file out of a folder that belongs to the old one.
        var other = await teacher.Client.PutAsJsonAsync($"/api/materials/{id}", new { title = "Moving", kind = "PastPaper" });
        Assert.Equal(JsonValueKind.Null, (await Json(other)).GetProperty("folderId").ValueKind);
        Assert.Equal("EduVibe/Grade 8/Past papers", Where());
        Assert.Equal("material_folder_invalid", await Code(await teacher.Client.PutAsJsonAsync($"/api/materials/{id}", new { title = "Moving", kind = "PastPaper", folderId = term })));

        // Back to the folder, and the file still opens.
        await teacher.Client.PutAsJsonAsync($"/api/materials/{id}", new { title = "Moving", kind = "Notes", folderId = term });
        Assert.Equal("EduVibe/Grade 8/Notes/Term 1", Where());
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.GetAsync($"/api/materials/{id}/file")).StatusCode);
    }

    [Fact]
    public async Task A_folder_deleted_in_Drive_is_made_again_when_a_file_is_added_to_it_and_Drive_trouble_is_reported()
    {
        var teacher = await ProTeacher("Gone");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client, "Grade 7");
        var term = (await NewFolder(teacher.Client, classId, "Gone term")).GetProperty("id").GetGuid();
        factory.Drive.Folders.Remove(factory.Drive.Folders.Single(f => f.Value.Name == "Gone term").Key);

        var form = Upload(classId, "back.pdf", kind: "Notes");
        form.Add(new StringContent(term.ToString()), "folderId");
        Assert.Equal(HttpStatusCode.Created, (await teacher.Client.PostAsync("/api/materials", form)).StatusCode);
        Assert.Equal("EduVibe/Grade 7/Notes/Gone term", DrivePath(factory.Drive, factory.Drive.Files.Values.Single(f => f.Name == "back.pdf").Folder));

        factory.Drive.FailWithCode = "drive_api_disabled";
        var refused = await teacher.Client.PostAsJsonAsync("/api/materials/folders", new { classId, kind = "Notes", name = "Term 2" });
        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        Assert.Equal("drive_api_disabled", await Code(refused));
        factory.Drive.FailWithCode = null;
        Assert.Equal(1, (await Json(await teacher.Client.GetAsync($"/api/materials/folders?classId={classId}"))).GetArrayLength());
    }

    [Fact]
    public async Task Folders_need_Drive_to_be_connected_and_belong_to_their_teacher_alone()
    {
        var teacher = await ProTeacher("Private");
        var rival = await ProTeacher("Nosy");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client);
        var folder = (await NewFolder(teacher.Client, classId, "Mine")).GetProperty("id").GetGuid();

        Assert.Equal(0, (await Json(await rival.Client.GetAsync("/api/materials/folders"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await rival.Client.PutAsJsonAsync($"/api/materials/folders/{folder}", new { name = "Taken" })).StatusCode);
        await rival.Client.DeleteAsync($"/api/materials/folders/{folder}");
        Assert.Equal(1, (await Json(await teacher.Client.GetAsync("/api/materials/folders"))).GetArrayLength());
        Assert.Equal("material_class_required", await Code(await rival.Client.PostAsJsonAsync("/api/materials/folders", new { classId, kind = "Notes", name = "x" })));

        var unconnected = await ProTeacher("Offline");
        var theirClass = await ClassOf(unconnected.Client);
        var notConnected = await unconnected.Client.PostAsJsonAsync("/api/materials/folders", new { classId = theirClass, kind = "Notes", name = "x" });
        Assert.Equal(HttpStatusCode.Conflict, notConnected.StatusCode);
        Assert.Equal("drive_not_connected", await Code(notConnected));

        var free = await factory.NewTeacherAsync("Freebie");
        Assert.Equal(HttpStatusCode.Forbidden, (await free.Client.GetAsync("/api/materials/folders")).StatusCode);
    }

    [Fact]
    public async Task A_student_sees_the_folders_a_material_is_in()
    {
        var teacher = await ProTeacher("Foldered");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client, "Folder class");
        var term = (await NewFolder(teacher.Client, classId, "Term 1")).GetProperty("id").GetGuid();
        var week = (await NewFolder(teacher.Client, classId, "Week 1", parentId: term)).GetProperty("id").GetGuid();
        var form = Upload(classId, "deep.pdf", title: "Deep", kind: "Notes");
        form.Add(new StringContent(week.ToString()), "folderId");
        await teacher.Client.PostAsync("/api/materials", form);
        await AddMaterial(teacher.Client, classId, "loose.pdf", "Loose", "Notes");
        var client = await SignInStudent(await Enrol(teacher.Client, classId, "Walker"));

        var list = (await Json(await client.GetAsync("/api/materials/mine"))).EnumerateArray().ToList();
        var deep = list.Single(i => i.GetProperty("material").GetProperty("title").GetString() == "Deep");
        Assert.Equal(["Term 1", "Week 1"], deep.GetProperty("folderPath").EnumerateArray().Select(n => n.GetString()!).ToArray());
        Assert.Equal(0, list.Single(i => i.GetProperty("material").GetProperty("title").GetString() == "Loose").GetProperty("folderPath").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/materials/folders")).StatusCode);
    }

    [Fact]
    public async Task A_file_is_rejected_when_it_is_the_wrong_type_too_big_or_for_a_class_that_is_not_theirs()
    {
        var teacher = await ProTeacher("Strict");
        var other = await ProTeacher("Other");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client);

        Assert.Equal("material_type_not_allowed", await Code(await teacher.Client.PostAsync("/api/materials", Upload(classId, "run.exe"))));
        Assert.Equal("material_type_not_allowed", await Code(await teacher.Client.PostAsync("/api/materials", Upload(classId, "noext"))));
        Assert.Equal("material_too_large", await Code(await teacher.Client.PostAsync("/api/materials", Upload(classId, "big.pdf", new byte[26 * 1024 * 1024]))));
        Assert.Equal("material_class_required", await Code(await teacher.Client.PostAsync("/api/materials", Upload(Guid.NewGuid(), "a.pdf"))));
        Assert.Equal("material_class_required", await Code(await teacher.Client.PostAsync("/api/materials", Upload(await ClassOf(other.Client), "a.pdf"))));
        Assert.Equal("material_invalid", await Code(await teacher.Client.PostAsync("/api/materials", Upload(classId, "a.pdf", kind: "Video"))));
        Assert.Equal("material_invalid", await Code(await teacher.Client.PostAsync("/api/materials", Upload(classId, "a.pdf", year: "1800"))));
        Assert.Equal("material_invalid", await Code(await teacher.Client.PostAsync("/api/materials", Upload(classId, "a.pdf", title: new string('t', 121)))));

        // With no title the file's own name is used, cut to fit.
        var untitled = await teacher.Client.PostAsync("/api/materials", Upload(classId, new string('n', 140) + ".pdf"));
        Assert.Equal(120, (await Json(untitled)).GetProperty("title").GetString()!.Length);
        Assert.DoesNotContain(factory.Drive.Files.Values, f => f.Name is "run.exe" or "noext" or "big.pdf");
    }

    [Fact]
    public async Task Adding_a_file_without_a_connection_or_after_Google_access_was_removed_asks_to_connect()
    {
        var teacher = await ProTeacher("Unlinked");
        var classId = await ClassOf(teacher.Client);
        Assert.Equal("drive_not_connected", await Code(await teacher.Client.PostAsync("/api/materials", Upload(classId, "a.pdf"))));

        await Connect(teacher.Client);
        factory.Drive.RevokedByTeacher = true;
        try
        {
            Assert.Equal("drive_reconnect", await Code(await teacher.Client.PostAsync("/api/materials", Upload(classId, "a.pdf"))));
            var status = await Json(await teacher.Client.GetAsync("/api/materials/drive"));
            Assert.True(status.GetProperty("needsReconnect").GetBoolean());
        }
        finally
        {
            factory.Drive.RevokedByTeacher = false;
        }

        await Connect(teacher.Client);
        Assert.False((await Json(await teacher.Client.GetAsync("/api/materials/drive"))).GetProperty("needsReconnect").GetBoolean());
        await AddMaterial(teacher.Client, classId);
    }

    [Fact]
    public async Task When_Google_says_what_is_wrong_the_teacher_is_told_that_and_not_just_that_Drive_is_unavailable()
    {
        var teacher = await ProTeacher("Told");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client);

        foreach (var code in new[] { "drive_api_disabled", "drive_storage_full", "drive_unavailable" })
        {
            factory.Drive.FailWithCode = code;
            try
            {
                var response = await teacher.Client.PostAsync("/api/materials", Upload(classId, "a.pdf"));
                Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
                Assert.Equal(code, await Code(response));
            }
            finally
            {
                factory.Drive.FailWithCode = null;
            }
        }

        // Nothing was saved by the failed attempts.
        Assert.Equal(0, (await Json(await teacher.Client.GetAsync("/api/materials"))).GetArrayLength());
    }

    [Fact]
    public async Task A_folder_the_teacher_deleted_in_Drive_is_made_again()
    {
        var teacher = await ProTeacher("Tidy");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client);
        await AddMaterial(teacher.Client, classId, "one.pdf");
        var gone = factory.Drive.Files.Values.Single(f => f.Name == "one.pdf").Folder;
        factory.Drive.Folders.Remove(gone);

        await AddMaterial(teacher.Client, classId, "two.pdf");
        var folder = factory.Drive.Files.Values.Single(f => f.Name == "two.pdf").Folder;
        Assert.NotEqual(gone, folder);
        Assert.True(factory.Drive.Folders.ContainsKey(folder));
    }

    [Fact]
    public async Task A_teacher_can_edit_open_and_remove_a_material_and_the_file_leaves_Drive()
    {
        var teacher = await ProTeacher("Editor");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client);
        var id = (await AddMaterial(teacher.Client, classId, "set.pdf")).GetProperty("id").GetGuid();

        var edited = await teacher.Client.PutAsJsonAsync($"/api/materials/{id}", new { title = "Set 2", kind = "Notes", topic = "Trig", year = 2023 });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("Set 2", (await Json(edited)).GetProperty("title").GetString());
        Assert.Equal("material_invalid", await Code(await teacher.Client.PutAsJsonAsync($"/api/materials/{id}", new { title = " ", kind = "Notes" })));

        var opened = await teacher.Client.GetAsync($"/api/materials/{id}/file");
        Assert.Equal("application/pdf", opened.Content.Headers.ContentType!.MediaType);
        Assert.NotEqual("attachment", opened.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("%PDF-1.4 hello", await opened.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", opened.Headers.GetValues("X-Content-Type-Options").Single());

        var download = await teacher.Client.GetAsync($"/api/materials/{id}/file?download=true");
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync($"/api/materials/{id}")).StatusCode);
        Assert.DoesNotContain(factory.Drive.Files.Values, f => f.Name == "set.pdf");
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.Client.GetAsync($"/api/materials/{id}/file")).StatusCode);
    }

    [Fact]
    public async Task A_material_whose_file_vanished_from_Drive_says_so_and_can_still_be_removed()
    {
        var teacher = await ProTeacher("Lost");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client);
        var id = (await AddMaterial(teacher.Client, classId)).GetProperty("id").GetGuid();
        factory.Drive.Files.Clear();

        Assert.Equal("material_file_missing", await Code(await teacher.Client.GetAsync($"/api/materials/{id}/file")));
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync($"/api/materials/{id}")).StatusCode);
    }

    [Fact]
    public async Task One_teacher_cannot_see_or_open_another_teachers_materials()
    {
        var teacher = await ProTeacher("Owner");
        var rival = await ProTeacher("Rival");
        await Connect(teacher.Client);
        await Connect(rival.Client);
        var id = (await AddMaterial(teacher.Client, await ClassOf(teacher.Client))).GetProperty("id").GetGuid();

        Assert.Equal(0, (await Json(await rival.Client.GetAsync("/api/materials"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await rival.Client.GetAsync($"/api/materials/{id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rival.Client.PutAsJsonAsync($"/api/materials/{id}", new { title = "x", kind = "Notes" })).StatusCode);
        await rival.Client.DeleteAsync($"/api/materials/{id}");
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.GetAsync($"/api/materials/{id}/file")).StatusCode);
    }

    [Fact]
    public async Task A_student_sees_and_opens_materials_of_their_classes_only_while_enrolled()
    {
        var teacher = await ProTeacher("Shared");
        await Connect(teacher.Client);
        var mine = await ClassOf(teacher.Client, "Mine");
        var others = await ClassOf(teacher.Client, "Not mine");
        var visible = (await AddMaterial(teacher.Client, mine, "visible.pdf", "Visible paper")).GetProperty("id").GetGuid();
        var hidden = (await AddMaterial(teacher.Client, others, "hidden.pdf", "Hidden paper")).GetProperty("id").GetGuid();
        var student = await Enrol(teacher.Client, mine, "Learner");
        var client = await SignInStudent(student);

        var list = await Json(await client.GetAsync("/api/materials/mine"));
        var item = Assert.Single(list.EnumerateArray());
        Assert.Equal("Visible paper", item.GetProperty("material").GetProperty("title").GetString());
        Assert.Equal("Mine", item.GetProperty("classTitle").GetString());
        Assert.Equal("Shared Teacher", item.GetProperty("teacherName").GetString());

        Assert.Equal("%PDF-1.4 hello", await (await client.GetAsync($"/api/materials/mine/{visible}/file")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/materials/mine/{hidden}/file")).StatusCode);

        // The teacher's own routes are closed to students.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/materials")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/materials/{visible}/file")).StatusCode);

        // Leaving the class ends access at once.
        await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{student.EnrolmentId}", new { status = "Left" });
        Assert.Equal(0, (await Json(await client.GetAsync("/api/materials/mine"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/materials/mine/{visible}/file")).StatusCode);
    }

    [Fact]
    public async Task An_archived_class_closes_its_files_to_students_and_restoring_it_opens_them_again()
    {
        var teacher = await ProTeacher("Archiver");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client);
        var id = (await AddMaterial(teacher.Client, classId)).GetProperty("id").GetGuid();
        var client = await SignInStudent(await Enrol(teacher.Client, classId, "Retired"));

        Assert.True((await teacher.Client.PostAsync($"/api/classes/classes/{classId}/archive", null)).IsSuccessStatusCode);
        Assert.Equal(0, (await Json(await client.GetAsync("/api/materials/mine"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/materials/mine/{id}/file")).StatusCode);
        Assert.Equal(1, (await Json(await teacher.Client.GetAsync($"/api/materials?classId={classId}"))).GetArrayLength());

        Assert.True((await teacher.Client.PostAsync($"/api/classes/classes/{classId}/restore", null)).IsSuccessStatusCode);
        Assert.Equal(1, (await Json(await client.GetAsync("/api/materials/mine"))).GetArrayLength());
    }

    [Fact]
    public async Task Students_lose_the_materials_when_their_teacher_is_no_longer_on_Pro()
    {
        var teacher = await ProTeacher("Lapsing");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client);
        var id = (await AddMaterial(teacher.Client, classId)).GetProperty("id").GetGuid();
        var client = await SignInStudent(await Enrol(teacher.Client, classId, "Watcher"));
        Assert.Equal(1, (await Json(await client.GetAsync("/api/materials/mine"))).GetArrayLength());

        var admin = await factory.AdminClientAsync();
        var plans = await Json(await admin.GetAsync("/api/plans/plans"));
        var free = plans.EnumerateArray().Single(p => p.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/plans/teachers/{teacher.Id}/subscription", new { planId = free, note = "back to free" })).StatusCode);

        Assert.Equal(0, (await Json(await client.GetAsync("/api/materials/mine"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/materials/mine/{id}/file")).StatusCode);
    }

    [Fact]
    public async Task Disconnecting_revokes_the_token_but_keeps_the_list_and_connecting_again_restores_it()
    {
        var teacher = await ProTeacher("Pauser");
        await Connect(teacher.Client);
        var classId = await ClassOf(teacher.Client);
        var id = (await AddMaterial(teacher.Client, classId)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync("/api/materials/drive")).StatusCode);
        Assert.Contains("refresh-ok", factory.Drive.RevokedTokens);
        Assert.False((await Json(await teacher.Client.GetAsync("/api/materials/drive"))).GetProperty("connected").GetBoolean());
        Assert.Equal(1, (await Json(await teacher.Client.GetAsync("/api/materials"))).GetArrayLength());
        Assert.Equal("drive_not_connected", await Code(await teacher.Client.GetAsync($"/api/materials/{id}/file")));

        await Connect(teacher.Client);
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.GetAsync($"/api/materials/{id}/file")).StatusCode);
    }

    [Fact]
    public async Task The_privacy_export_lists_the_materials_but_never_the_Google_token_and_erasing_a_teacher_revokes_it()
    {
        var teacher = await ProTeacher("Exported");
        var admin = await factory.AdminClientAsync();
        await Connect(teacher.Client, code: "export-me");
        await AddMaterial(teacher.Client, await ClassOf(teacher.Client), "exported.pdf", "Exported paper");

        var export = await (await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).Content.ReadAsStringAsync();
        Assert.Contains("Exported paper", export);
        Assert.Contains("teacher@example.com", export);
        Assert.DoesNotContain("export-me", export);
        Assert.DoesNotContain("refresh", export, StringComparison.OrdinalIgnoreCase);

        Assert.True((await admin.PutAsJsonAsync($"/api/identity/teachers/{teacher.Id}/active", new { isActive = false })).IsSuccessStatusCode);
        var erased = await admin.PostAsJsonAsync($"/api/privacy/teachers/{teacher.Id}/erase", new { confirm = "Exported Teacher" });
        Assert.True(erased.IsSuccessStatusCode, await erased.Content.ReadAsStringAsync());
        Assert.Contains("refresh-export-me", factory.Drive.RevokedTokens);
        Assert.DoesNotContain("Exported paper", await (await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).Content.ReadAsStringAsync());
    }
}
