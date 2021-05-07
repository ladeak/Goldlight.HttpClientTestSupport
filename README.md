# Testing HttpClient
## Scenario
We have a series of unit tests and, in them, we want to test
a request from `HttpClient` but, in the spirit of true isolated tests, we don't 
want to actually perform the HTTP requests. We need some means of replacing
the real call to the end point with a fake endpoint in its place.

The problem in this scenario is that there is no apparently simple 
way to mock `HttpClient`. There is no `IHttpClient` interface available
to us, that will allow us to set up a mock and the calls we are interested aren't virtual so mocking them
is difficult in most mock frameworks. With this apparent limitation
in mind, how can we easily mock calls such as `PostAsync`? 

To answer this question, we need to understand how `HttpClient` actually
manages HTTP requests. If we look at the source code for `GetAsync` in
the [reference source](https://github.com/microsoft/referencesource/blob/master/System/net/System/Net/Http/HttpClient.cs)
we see that the method calls out to `SendAsync`.
```csharp
public Task<HttpResponseMessage> GetAsync(Uri requestUri, HttpCompletionOption completionOption,
  CancellationToken cancellationToken)
{
  return SendAsync(new HttpRequestMessage(HttpMethod.Get, requestUri), completionOption, cancellationToken);
}
```
`HttpClient` inherits from a class called [HttpMessageInvoker](https://github.com/microsoft/referencesource/blob/5697c29004a34d80acdaf5742d7e699022c64ecd/System/net/System/Net/Http/HttpMessageInvoker.cs#L11)
which has `SendAsync` as a virtual method inside it. The `HttpMessageInvoker` class
requires posting an `HttpMessageHandler` instance it via a constructor.
This is important to because `SendAsync` calls out to the `HttpMessageHandler.SendAsync` method
and this is the "touch point" that we want to interact with to mock our REST call.
## Faking the message handler
An issue that we have to cope with, with regards to testing the message handler is the problem
that the virtual `SendAsync` method is protected and most mocking frameworks are unable to
mock virtual methods. While Moq provides the ability to mock protected properties, if we 
are using a framework like FakeItEasy, then we can't directly interact with the protected
method.

All of this detective work has told us that, in order to replace our calls, we need to inherit
from `HttpMessageHandler` and provide our own implementation that satisfies the ability to
simulate web requests.

Rather than making a mockable object, we are going to provide a `FakeHttpMessageHandler` that
allows us to control what the response we receive back contains.
### Basic usage
The `FakeHttpMessageHandler` implementation makes certain assumptions when it is instantiated.
Instantiation sets the expectation that our response message will be set to version 1.0 and the
status code defaults to 200. We have the ability to override these values if we need to using
a fluent API.

We are going to start with a simple xUnit test. In all of the examples we are going to
see here, we are using xUnit but any testing framework will do. Our examples will also
avoid using any mocking framework to show how we can easily test our code.
```csharp
[Fact]
public async Task EnsureOkStatusIsReturned()
{
  HttpClient httpClient = new HttpClient(new FakeHttpMessageHandler());
  HttpResponseMessage response = await httpClient.GetAsync("https://mydummy.url/");
  Assert.Equals(HttpStatusCode.OK, response.StatusCode);
}
```
### Dealing with response content
Obviously, when we are dealing with a GET call, we really want to see some content coming
back. Let's see how we can set a test up to deal with this.

First, we want to create a model that we are going to return.
```csharp
public sealed class SampleModel
{
  public string FirstName => "Stan";
  public string LastName => "Lee";
  public string FullName => FirstName + " " + LastName;
}
```
With this class in place, what might our test look like? In this test, we see our fluent API in action setting up our fake handler with our model
as the expected response content.
```csharp
[Fact]
public async Task GivenValidRequestWithModelContentExpected_WhenGetIsCalled_ThenContentIsSetToModel()
{
  FakeHttpMessageHandler fake = new FakeHttpMessageHandler().WithExpectedContent(new SampleModel());
  HttpClient httpClient = new HttpClient(fake);
  HttpResponseMessage responseMessage = await httpClient.GetAsync("https://dummyaddress.com/someapi");
  SampleModel converted =
    JsonConvert.DeserializeObject<SampleModel>(await responseMessage.Content.ReadAsStringAsync());
  Assert.Equal("Stan Lee", converted.FullName);
}
```
### Returning different status codes
With GET calls, we should probably test to see what happens in our code when we get a
404 NotFound status. The question is, how would we set this up? 

We are going to start by creating an example that is a little bit more realistic. We want to 
see our faked message handler in action. The best way to see that is to see the handler
in use outside of a unit test. Let's create a class that might be called by a web controller.
```csharp
public class ExampleControllerHandling
{
  private readonly HttpClient _httpClient;
  private const string BaseUrl = "http://www.goldlight-dummy.com/api/sample/";
  public ExampleControllerHandling(HttpClient httpClient)
  {
    _httpClient = httpClient;
  }

  public async Task<SampleModel> GetById(Guid id)
  {
    HttpResponseMessage response = await _httpClient.GetAsync(BaseUrl + id);
    switch (response.StatusCode)
    {
      case HttpStatusCode.OK:
        return JsonConvert.DeserializeObject<SampleModel>(await response.Content.ReadAsStringAsync());
      case HttpStatusCode.BadRequest:
        throw new Exception("Unable to find " + id);
    }
    return null;
  }
}
```
Let's create a test that exercises the `BadRequest` path through our code.
```csharp
[Fact]
public async Task GivenComplexController_WhenBadRequestIsExpected_ThenBadRequestIsHandled()
{
  FakeHttpMessageHandler fake = new FakeHttpMessageHandler().WithStatusCode(HttpStatusCode.BadRequest);
  HttpClient httpClient = new HttpClient(fake);
  ExampleControllerHandling exampleController = new ExampleControllerHandling(httpClient);
  int called = 0;
  try
  {
    await exampleController.GetById(Guid.NewGuid());
  }
  catch (Exception e)
  {
    if (e.Message.StartsWith("Unable to find "))
    {
      called++;
    }
  }

  Assert.Equal(1, called);
}
```
As we saw in the code sample, the expected status code is added to our handler using the
`WithStatusCode` call.

### Handling Headers
It is not uncommon for out code to have to deal with response headers. To see how this works in practice,
let's add a new method to the ExampleControllerHandling sample to return a list of all values
when the status code is 200 and we have a response header called *order66* that contains the value
*babyyoda*.
```csharp
public async Task<IEnumerable<SampleModel>> GetAll()
{
  HttpResponseMessage response = await _httpClient.GetAsync(BaseUrl);
  if (response.StatusCode == HttpStatusCode.OK && response.Headers.TryGetValues("order66", out IEnumerable<string> headers))
  {
    if (headers.First() == "babyyoda")
    {
      return JsonConvert.DeserializeObject<IEnumerable<SampleModel>>(await response.Content.ReadAsStringAsync());
    }
  }

  return null;
}
```
To add the header, we are going to use `WithResponseHeader` to add a key/value pair as
a header with our response. As we know that our message handler can be built with a fluent API,
we are going to add multiple parts with one chain.
```csharp
[Fact]
public async Task GivenMultipleInputsIntoController_WhenProcessing_ThenModelIsReturned()
{
  List<SampleModel> sample = new List<SampleModel>() {new SampleModel(), new SampleModel()};
  FakeHttpMessageHandler fake = new FakeHttpMessageHandler().WithStatusCode(HttpStatusCode.OK)
    .WithResponseHeader("order66", "babyyoda").WithExpectedContent(sample);
  HttpClient httpClient = new HttpClient(fake);
  ExampleControllerHandling exampleController = new ExampleControllerHandling(httpClient);
  IEnumerable<SampleModel> output = await exampleController.GetAll();
  Assert.Equal(2, output.Count());
}
```
We can see in this sample that we are explicitly setting the status code (okay, while it's the default
I wanted to demonstrate adding multiple parts in one go) and the response header that we
expect to trigger our response.

As well as being able to set a single value for a header, there is an override that accepts
an array of values.

#### Trailing headers
In the same way we can set a header in a response, NetStandard2.1 gives us the ability
to add [trailing headers](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpresponsemessage.trailingheaders?view=net-5.0) in a response. This feature is only avaiable in targets that support
.NetStandard2.1 such as .NET Core 3.1 and .NET 5. Use `WithTrailingResponseHeader` to add the
trailing headers.

### Additional
If we need to set version numbers for our REST calls, we have `WithVersion` to provide 
version information.
