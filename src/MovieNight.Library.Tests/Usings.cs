global using FluentAssertions;

global using System.Collections.Immutable;
global using System.Net;

global using Amazon.DynamoDBv2;
global using Amazon.DynamoDBv2.Model;
global using Amazon.SimpleEmail;
global using Amazon.SimpleEmail.Model;

global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging.Abstractions;

global using NSubstitute;
global using NSubstitute.ExceptionExtensions;

global using MovieNight.Library.Extensions;
global using MovieNight.Library.Messaging;
global using MovieNight.Library.Models;
global using MovieNight.Library.Providers;
global using MovieNight.Library.Services;
global using MovieNight.Library.Tests.Fixtures;
global using MovieNight.Library.Utility;
global using MovieNight.Library.Validators;

global using Xunit;
