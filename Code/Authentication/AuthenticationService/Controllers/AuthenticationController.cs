﻿using Authentication.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace AuthenticationService.Controllers
{
    [Authorize]
    [Route("/api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly IJWTManagerRepository jWTManager;
        private readonly IUserServiceRepository userServiceRepository;

        public UsersController(IJWTManagerRepository jWTManager, IUserServiceRepository userServiceRepository)
        {
            this.jWTManager = jWTManager;
            this.userServiceRepository = userServiceRepository;
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("authenticate")]
        public async Task<IActionResult> AuthenticateAsync(Users usersdata)
        {
            var validUser = await userServiceRepository.IsValidUserAsync(usersdata);

            if (!validUser)
            {
                return Unauthorized("Incorrect username or password!");
            }

            var token = jWTManager.GenerateToken(usersdata.Name);

            if (token == null)
            {
                return Unauthorized("Invalid Attempt!");
            }

            // saving refresh token to the db
            UserRefreshTokens obj = new UserRefreshTokens
            {
                RefreshToken = token.Refresh_Token,
                UserName = usersdata.Name
            };

            userServiceRepository.AddUserRefreshTokens(obj);
            userServiceRepository.SaveCommit();
            return Ok(token);
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("refresh")]
        public IActionResult Refresh(Tokens token)
        {
            var principal = jWTManager.GetPrincipalFromExpiredToken(token.Access_Token);
            var username = principal.Identity?.Name;

            //retrieve the saved refresh token from database
            var savedRefreshToken = userServiceRepository.GetSavedRefreshTokens(username, token.Refresh_Token);

            if (savedRefreshToken.RefreshToken != token.Refresh_Token)
            {
                return Unauthorized("Invalid attempt!");
            }

            var newJwtToken = jWTManager.GenerateRefreshToken(username);

            if (newJwtToken == null)
            {
                return Unauthorized("Invalid attempt!");
            }

            // saving refresh token to the db
            UserRefreshTokens obj = new UserRefreshTokens
            {
                RefreshToken = newJwtToken.Refresh_Token,
                UserName = username
            };

            userServiceRepository.DeleteUserRefreshTokens(username, token.Refresh_Token);
            userServiceRepository.AddUserRefreshTokens(obj);
            userServiceRepository.SaveCommit();

            return Ok(newJwtToken);
        }
    }

}
