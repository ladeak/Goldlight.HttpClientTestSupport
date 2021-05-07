using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Goldlight.HttpClientTestSupport;
using Xunit;

namespace Goldlight.HttpClientTestSupportTests
{
  public class ExampleControllerHandlingTests
  {
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
  }
}